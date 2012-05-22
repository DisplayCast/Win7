// Copyright (c) 2012, Fuji Xerox Co., Ltd.
// All rights reserved.
// Author: Surendar Chandra, FX Palo Alto Laboratory, Inc.

using System;
using System.Threading;
using System.Collections;
using System.Diagnostics;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Drawing.Imaging;
using System.Text;
using System.Windows.Forms;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.IO;
using System.IO.Compression;
using System.Runtime;

#if USE_BLUETOOTH
using InTheHand.Net;
using InTheHand.Net.Sockets;
using InTheHand.Windows.Forms;
using InTheHand.Net.Bluetooth;
#endif

using Microsoft.Win32;
using Mirror.Driver;

using ZeroconfService;
using Shared;

namespace FXPAL.DisplayCast.Streamer {
    public class Program {
        static private NetService publishService = null;
        static private readonly DesktopMirror _mirror = new DesktopMirror();
        static private streamThread streamer;
        static private Hashtable TXTrecords = new Hashtable();

        static public int maskX;
        static public int maskY;
        static public int maskWidth;
        static public int maskHeight;
        static public Boolean maskValid = false;

        #region Bonjour publish helpers
        /// <summary>
        /// 
        /// </summary>
        /// <param name="service"></param>
        static private void publishService_DidPublishService(NetService service) {
            System.Console.WriteLine("Published Bonjour Service: domain({0}) type({1}) name({2})", service.Domain, service.Type, service.Name);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="service"></param>
        /// <param name="exception"></param>
        static private void publishService_DidNotPublishService(NetService service, DNSServiceException exception) {
            MessageBox.Show(String.Format("A DNSServiceException: {0}", exception.Message), "Error!", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        /// <summary>
        /// 
        /// </summary>
        static private void publishService_StopPublish() {
            if (publishService != null) {
                publishService.Stop();
            }
        }
        #endregion

        #region Update callback from mirrror driver
        static private int numUpdates = 0;
        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="dce"></param>
        static private unsafe void _DesktopChange(object sender, DesktopChangeEventArgs dce) {
            if (streamer.clients.Count > 0) {   // No one is listening, why bother to process updates
                try {
                     lock (streamer.updates.SyncRoot) {
                        if (streamer.updates.Count == 0) {
                            if (numUpdates++ > 5) {
                                // Send the iFrame

                                GCbuf screenbuf = new GCbuf();
#if USE_BITMAP_COMPRESS
                                int ptr = DesktopMirror._bitmapWidth * DesktopMirror._bitmapHeight, num = 0;
                                for (int i = 0; i < ptr; i++)
                                    screenbuf.buf[num++] = 0xFF;
                                for (int i = 0; i < (ptr * 4); i += 4) {
                                    screenbuf.buf[num++] = _mirror.screen[i];
                                    screenbuf.buf[num++] = _mirror.screen[i + 1];
                                    screenbuf.buf[num++] = _mirror.screen[i + 2];
                                }
#else
                            Buffer.BlockCopy(_mirror.screen, 0, screenbuf.buf, 0, _mirror.screen.Length);
                            //    for (int i = 0; i < _mirror.screen.Length; i++)
                            //        screenbuf.buf[i] = _mirror.screen[i];
#endif
                                screenbuf.Length = _mirror.screen.Length;

                                streamer.updates.Enqueue(new sendUpdate(null, screenbuf, maskX, maskY, maskWidth, maskHeight));
                                numUpdates = 0;
                            } else {
                                if (maskValid == true) {
                                    System.Drawing.Rectangle cur = new System.Drawing.Rectangle(dce.x, dce.y, dce.w, dce.h);
                                    System.Drawing.Rectangle mask = new System.Drawing.Rectangle(Program.maskX, Program.maskY, Program.maskWidth, Program.maskHeight);
                                    cur.Intersect(mask);

                                    if (cur.IsEmpty)
                                        return;
                                    else
                                        streamer.updates.Enqueue(new sendUpdate(null, null, cur.X, cur.Y, cur.Width, cur.Height));
                                } else
                                    streamer.updates.Enqueue(new sendUpdate(null, null, dce.x, dce.y, dce.w, dce.h));
                            }
                        } else {
                            System.Drawing.Rectangle orig = new System.Drawing.Rectangle(dce.x, dce.y, dce.w, dce.h);

                            if (maskValid == true) {
                                System.Drawing.Rectangle mask = new System.Drawing.Rectangle(Program.maskX, Program.maskY, Program.maskWidth, Program.maskHeight);

                                orig.Intersect(mask);
                                if (orig.IsEmpty)
                                    return;
                            }

                            for (int i = streamer.updates.Count; i > 0; i--) {
                                sendUpdate upd = (sendUpdate)streamer.updates.Dequeue();
                                System.Drawing.Rectangle cur = new System.Drawing.Rectangle(upd.x, upd.y, upd.w, upd.h);
                                /* I don't think that I need to do this 
                                if (maskValid == true) {
                                    cur.Intersect(mask);
                                    if (cur.IsEmpty)
                                        continue;
                                }
                                 */

                                cur.Intersect(orig);
                                if (cur.IsEmpty) {
                                    // Move this update to the back of the line? Okay because all pending updates are disjoint
                                    streamer.updates.Enqueue(upd);
                                    // Moved to the front of the for loop
                                    // upd = (sendUpdate)streamer.updates.Dequeue();
                                } else {
                                    cur.X = upd.x;
                                    cur.Y = upd.y;
                                    cur.Width = upd.w;
                                    cur.Height = upd.h;

                                    System.Drawing.Rectangle combined = System.Drawing.Rectangle.Union(orig, cur);
                                    Boolean anymoreCombine = true;
                                    // int count = 0;
                                    while (anymoreCombine) {
                                        anymoreCombine = false;

                                        for (int j = streamer.updates.Count; j > 0; j--) {
                                            sendUpdate u = (sendUpdate)streamer.updates.Dequeue();
                                            System.Drawing.Rectangle nxt = new System.Drawing.Rectangle(u.x, u.y, u.w, u.h);
                                            /* I don't think that I need this either
                                            if (maskValid == true) {
                                                nxt.Intersect(mask);
                                                if (nxt.IsEmpty)
                                                    continue;
                                            }
                                             */ 
                                            nxt.Intersect(combined);
                                            if (nxt.IsEmpty) 
                                                streamer.updates.Enqueue(u);
                                            else {
                                                nxt.X = u.x;
                                                nxt.Y = u.y;
                                                nxt.Width = u.w;
                                                nxt.Height = u.h;

                                                combined = System.Drawing.Rectangle.Union(combined, nxt);
                                                anymoreCombine = true;

                                                Trace.WriteLine("DEBUG: ----");
                                                break;
                                            }
                                        }
                                    }

                                    streamer.updates.Enqueue(new sendUpdate(null, null, combined.X, combined.Y, combined.Width, combined.Height));
                                    // Trace.WriteLine("DEBUG: need to make sure that this update does not overlap with prior non-overlapping rectangles");
                                    // Actually don't need to because of the way that Queue's work?
                                    return;
                                }
                            }
                            if (maskValid == true) {
                                System.Drawing.Rectangle cur = new System.Drawing.Rectangle(dce.x, dce.y, dce.w, dce.h);
                                System.Drawing.Rectangle mask = new System.Drawing.Rectangle(Program.maskX, Program.maskY, Program.maskWidth, Program.maskHeight);
                                cur.Intersect(mask);

                                if (cur.IsEmpty)
                                    return;
                                streamer.updates.Enqueue(new sendUpdate(null, null, cur.X, cur.Y, cur.Width, cur.Height));
                            } else
                                streamer.updates.Enqueue(new sendUpdate(null, null, dce.x, dce.y, dce.w, dce.h));
                        }
                    }
                } catch (System.IO.IOException) {
                    Trace.WriteLine("DEBUG: An internal Win32 exception is caused by update while creating event handler. Ignoring");
                    // Environment.Exit(1);
                } catch (Exception e) {
                    MessageBox.Show("DEBUG: Error while capturing update coordinates " + e.StackTrace);
                }
            }
        }
        #endregion

        #region Remote control processing for MASK command
        /// <summary>
        /// 
        /// </summary>
        /// <param name="ar"></param>
        public static void ctrlBeginAcceptTcpClient(IAsyncResult ar) {
            TcpListener ctrlAddr = (TcpListener)ar.AsyncState;
            TcpClient clnt = ctrlAddr.EndAcceptTcpClient(ar);
            NetworkStream strm = clnt.GetStream();
            char[] delimiters = { ' ', '\t', '\r', '\n' };
            byte[] bytes = new byte[clnt.ReceiveBufferSize];

            ctrlAddr.BeginAcceptTcpClient(ctrlBeginAcceptTcpClient, ctrlAddr);
            // strm.BeginRead(bytes, 0, clnt.ReceiveBufferSize, ctrlCmdRead, state);

            strm.ReadTimeout = 30000;
            try {
                strm.Read(bytes, 0, clnt.ReceiveBufferSize);
            } catch (System.IO.IOException ioe) {
                Trace.WriteLine("FATAL: Timeout waiting for remote control: " + ioe.Message);
                strm.Close();
                return;
            }

            String cmdString = Encoding.UTF8.GetString(bytes);
            String[] words = cmdString.Split(delimiters);
            String cmd = words[0].ToUpper();

            switch (cmd) {
                case "MASK":
                case "CREATEREGION":
                    maskX = Convert.ToInt32(words[1]);
                    maskY = Convert.ToInt32(words[2]);
                    maskWidth = Convert.ToInt32(words[3]);
                    maskHeight = Convert.ToInt32(words[4]);

                    if ((maskX == 0) && (maskY == 0) && (maskWidth == DesktopMirror._bitmapWidth) && (maskHeight == DesktopMirror._bitmapHeight)) {
                        bytes = Encoding.ASCII.GetBytes("Mask unset");
                        strm.Write(bytes, 0, bytes.Length);
                        strm.Close();

                        maskValid = false;
                    } else {
                        bytes = Encoding.ASCII.GetBytes("Mask set to: " + maskX + "x" + maskY + " " + maskWidth + "x" + maskHeight);
                        strm.Write(bytes, 0, bytes.Length);
                        strm.Close();
                        maskValid = true;
                    }

                    if (publishService != null) {
                        if (TXTrecords.ContainsKey("maskScreen"))
                            TXTrecords.Remove("maskScreen");

                        if (maskValid)
                            TXTrecords.Add("maskScreen", maskX + "x" + maskY + " " + maskWidth + "x" + maskHeight);
                        publishService.TXTRecordData = NetService.DataFromTXTRecordDictionary(TXTrecords);
                        publishService.Publish();
                    }
                    return;

                default:
                    bytes = Encoding.ASCII.GetBytes(DisplayCastGlobals.STREAMER_CMD_SYNTAX_ERROR);
                    strm.Write(bytes, 0, bytes.Length);
                    strm.Close();
                    break;
            }
        }
        #endregion

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="args"></param>
        static void MyDefaultHandler(object sender, UnhandledExceptionEventArgs args) {
            var exception = (Exception)args.ExceptionObject;
            MessageBox.Show("FATAL: Unhandled - " + exception.StackTrace, "ERROR", MessageBoxButtons.OK, MessageBoxIcon.Error);
            Environment.Exit(1);
        }

        [MTAThread]
        static void Main() {
            // Catchall for all uncaught exceptions !!
            AppDomain currentDomain = AppDomain.CurrentDomain;
            currentDomain.UnhandledException += new UnhandledExceptionEventHandler(MyDefaultHandler);

            // Catch hibernation event so that we can reload the driver
            SystemEvents.PowerModeChanged += new PowerModeChangedEventHandler(DesktopMirror.SystemEvents_PowerModeChanged);

            // Setting some garbage collector parameters
            GCSettings.LatencyMode = GCLatencyMode.LowLatency;
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            // First load the mirror driver
            try {
                _mirror.Load();
            } catch (System.Security.SecurityException se) {
                MessageBox.Show(se.Message + ". You need to run this program as an administrator",
                    "ERROR", MessageBoxButtons.OK, MessageBoxIcon.Error);
                System.Environment.Exit(1);
            } catch (Exception ex) {
                MessageBox.Show("FATAL: Loading mirror driver failed. Check http://displaycast.fxpal.net/ for further instructions " + ex.StackTrace, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                // _mirror.Dispose();
                System.Environment.Exit(1);
            }

            // Nest, create a TCP port to listen for new Player connections
            TcpListener serverAddr = new TcpListener(new IPEndPoint(IPAddress.Any, 0));
            serverAddr.Start();

            // Now create a thread to process new client requests
            streamer = new streamThread(_mirror);
            serverThread nt = new serverThread(serverAddr, _mirror, streamer);

            Thread netThread = new Thread(new ThreadStart(nt.process));
            netThread.Name = "processNewClient";
            netThread.Start();

            // Create a thread to send data to the connected clients
            Thread strmThread = new Thread(new ThreadStart(streamer.process));
            strmThread.Name = "dataStreamer";
            // strmThread.Priority = ThreadPriority.Highest;
            strmThread.Start();

            // Now create a listener for snapshots - hardwired to port 9854
            try {
                HttpListener imageListener = new HttpListener();
                
                imageListener.Prefixes.Add("http://+:9854/");
                imageListener.Start();
                imageListener.BeginGetContext(_mirror.sendScreenShot, imageListener);

                if (imageListener.IsListening)
                    TXTrecords.Add("imagePort", "9854");
            } catch (Exception e) {
                MessageBox.Show("Oops " + e.Message);
            }

            maskX = maskY = 0;
            maskWidth = DesktopMirror._bitmapWidth;
            maskHeight = DesktopMirror._bitmapHeight;

            // Now listen for mask requests - perhaps I could've reused the imageport also
            TcpListener ctrlAddr = new TcpListener(IPAddress.Any, 0);   // Used for accepting MASK command
            ctrlAddr.Start();
            IPEndPoint sep = (IPEndPoint)ctrlAddr.LocalEndpoint;
            Debug.Assert(sep.Port != 0);
            TXTrecords.Add("maskPort", sep.Port.ToString());
            ctrlAddr.BeginAcceptTcpClient(ctrlBeginAcceptTcpClient, ctrlAddr);

            /* Fill TXT RECORD */
            TXTrecords.Add("screen", "0x0 " + DesktopMirror._bitmapWidth.ToString() + "x" + DesktopMirror._bitmapHeight.ToString());
            TXTrecords.Add("machineName", System.Environment.MachineName);
            TXTrecords.Add("osVersion", System.Environment.OSVersion.VersionString);
            TXTrecords.Add("version", Shared.DisplayCastGlobals.DISPLAYCAST_VERSION.ToString());
            TXTrecords.Add("userid", System.Environment.UserName);

#if USE_BLUETOOTH
            if (BluetoothRadio.IsSupported) {
                try {
                    TXTrecords.Add("bluetooth", BluetoothRadio.PrimaryRadio.LocalAddress.ToString("C").Replace(":", "-").ToLower());
                } catch {
                    TXTrecords.Add("bluetooth", "NotSupported");
                }
            } else
#endif
                TXTrecords.Add("bluetooth", "NotSupported");
            TXTrecords.Add("nearby", "UNKNOWN");

            String myName = null, id;
            using (RegistryKey dcs = Registry.CurrentUser.CreateSubKey("Software").CreateSubKey("FXPAL").CreateSubKey("DisplayCast").CreateSubKey("Streamer")) {
                if (dcs.GetValue("uid") == null)
                    dcs.SetValue("uid", System.Guid.NewGuid().ToString("D"));
                id = dcs.GetValue("uid").ToString();

                if (dcs.GetValue("Name") == null)
                    dcs.SetValue("Name", System.Environment.UserName);
                myName = dcs.GetValue("Name").ToString();
                TXTrecords.Add("name", myName);
            }

            try {
                // Now, publish my info via Bonjour
                IPEndPoint serv = (IPEndPoint)serverAddr.LocalEndpoint;

                publishService = new NetService(Shared.DisplayCastGlobals.BONJOURDOMAIN, Shared.DisplayCastGlobals.STREAMER, id, serv.Port);
                publishService.DidPublishService += new NetService.ServicePublished(publishService_DidPublishService);
                publishService.DidNotPublishService += new NetService.ServiceNotPublished(publishService_DidNotPublishService);
                publishService.TXTRecordData = NetService.DataFromTXTRecordDictionary(TXTrecords);
                publishService.Publish();
            } catch (Exception e) {
                Trace.WriteLine(e.StackTrace);
                MessageBox.Show("Apple Bonjour not installed. Pick up a local copy from http://displaycast.fxpal.net/",
                   "FATAL", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    Environment.Exit(1);
            }

            _mirror.DesktopChange += _DesktopChange;
            try {
                Application.Run(new Console(publishService, TXTrecords, id));
            } catch (Exception e) {
                MessageBox.Show("FATAL: " + e.Message, "ERROR", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}

#if OLD
        static private String getMyAddress() {
            Socket s = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

            s.Connect("www.fxpal.com", 80);
            IPEndPoint ip = (IPEndPoint)s.LocalEndPoint;
            s.Close();

            return ip.Address.ToString();
        }
#endif

