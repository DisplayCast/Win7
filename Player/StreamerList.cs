// Copyright (c) 2012, Fuji Xerox Co., Ltd.
// All rights reserved.
// Author: Surendar Chandra, FX Palo Alto Laboratory, Inc.

using System;
using System.Drawing;
using System.Collections;
using System.ComponentModel;
using System.Windows.Forms;
using System.Data;
using System.Text;
using System.Threading;
using System.Net;
using System.Net.Sockets;
using Microsoft.Win32;
using System.Diagnostics;
using System.Collections.Generic;
using System.Linq;
using System.Net.NetworkInformation;

using Microsoft.VisualBasic;

using InTheHand.Net;
using InTheHand.Net.Sockets;
using InTheHand.Windows.Forms;
using InTheHand.Net.Bluetooth;

using Shared;
using ZeroconfService;
using Location;
using location;
using Player;

// browserList needs to be static. Everytime you edit the GUI designer, it revers back to non-static
namespace FXPAL.DisplayCast.Player {
    public class StreamerList : Form {
        static NetService nsPublisher = null;
        private Container components = null;
        private static Hashtable TXTrecords = new Hashtable();
        private static ArrayList streamers = new ArrayList();
        private NetServiceBrowser nsBrowser;
#if  !PLAYER_TASKBAR
        private TextBox myName;
        private CheckBox locationDisclose;
#endif

#if PLAYER_TASKBAR
        private static ContextMenu contextMenu;
        private NotifyIcon notifyIcon;
        private static MenuItem exitItem, changeNameItem, locationItem, aboutItem, streamersItem;

        private IContainer iComponents;

        /// <summary>
        /// 
        /// </summary>
        /// <param name="Sender"></param>
        /// <param name="e"></param>
        private void locationChecked(object Sender, EventArgs e) {
            if (locationItem.Checked) {
                locationItem.Checked = false;

                prevLoc = null;
                TXTrecords.Remove("locationID");
                nsPublisher.TXTRecordData = NetService.DataFromTXTRecordDictionary(TXTrecords);
            } else {
                locationItem.Checked = true;
                discloseLocation = true;
            }
            using (RegistryKey dcs = Registry.CurrentUser.CreateSubKey("Software").CreateSubKey("FXPAL").CreateSubKey("DisplayCast").CreateSubKey("Streamer")) {
                dcs.SetValue("discloseLocation", locationItem.Checked);
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="Sender"></param>
        /// <param name="e"></param>
        private void about(object Sender, EventArgs e) {
            // var results = MessageBox.Show("FXPAL DisplayCast Player (v " + Shared.DisplayCastGlobals.DISPLAYCAST_VERSION.ToString() + "))", "FXPAL DisplayCast Player", MessageBoxButtons.OK);
            AboutBox about = new AboutBox();
            var results = about.ShowDialog();
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="Sender"></param>
        /// <param name="e"></param>
        private void exitItem_Click(object Sender, EventArgs e) {
            Environment.Exit(0);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="Sender"></param>
        /// <param name="e"></param>
        private void notifyIcon_DoubleClick(object Sender, EventArgs e) {
            if (this.WindowState == FormWindowState.Minimized)
                this.WindowState = FormWindowState.Normal;

            Activate();
        }
#else
        private static ListView browserList;
        private Label nameLabel;
#endif
           
        #region Location disclosure stuff
        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        public static String getWIFIMACAddress() {
            NetworkInterface[] nics = NetworkInterface.GetAllNetworkInterfaces();

            if (nics == null || nics.Length < 1)
                return null;

            foreach (NetworkInterface adapter in nics) {
                if (adapter.NetworkInterfaceType != NetworkInterfaceType.Wireless80211)
                    continue;

                if (adapter.Description.Contains("Virtual"))
                    continue;       // WTF - Displaycast desktop has a virtual WiFi adapter

                PhysicalAddress address = adapter.GetPhysicalAddress();
                byte[] bytes = address.GetAddressBytes();
                if (bytes.Length <= 0)
                    continue;

                String mac = "";
                for (int i = 0; i < bytes.Length; i++) {
                    mac += bytes[i].ToString("X2");
                    if (i != bytes.Length - 1)
                        mac += ":";
                }
                return (mac);
            }

            return null;
        }

        private static Boolean discloseLocation = false;
        
        static QueryMSE MSE;
        static String myMac;
        static Thread locThread;
        static AesMobileStationLocation prevLoc = null;

        /// <summary>
        /// 
        /// </summary>
        public static void monitorMyLocation() {
           /* TextBox locationString = (TextBox)o;
            if (locationString.GetType() != typeof(TextBox)) {
                Trace.WriteLine("Hmmm, not the right type");
                return;
            }
            */
            while (true) {
                using (RegistryKey dcs = Registry.CurrentUser.CreateSubKey("Software").CreateSubKey("FXPAL").CreateSubKey("DisplayCast").CreateSubKey("Player")) {

                    discloseLocation = Convert.ToBoolean(dcs.GetValue("discloseLocation", false));
                }

                if (discloseLocation) {
                    // Trace.WriteLine("DEBUG: Looking up location info..");
                    try {
                        MSE.login();
                        AesMobileStationLocation loc = MSE.queryMAC(myMac);
                        MSE.logout();

                        if (loc != null) {
                            if ((prevLoc == null) || (prevLoc.x != loc.x) || (prevLoc.y != loc.y)) {
                                TXTrecords.Add("locationID", "NOTIMPL" /* loc.x + " x " + loc.y */);
                                nsPublisher.TXTRecordData = NetService.DataFromTXTRecordDictionary(TXTrecords);

                                Trace.WriteLine(" Mac: " + loc.macAddress + " Loc: " + loc.x + "x" + loc.y + " lastHeard " + loc.minLastHeardSecs + " conf " + loc.confidenceFactor);
                                prevLoc = loc;
                            }
                        }
                    } catch (Exception e) {
                        Trace.WriteLine("DEBUG: Disclose location: " + e.StackTrace );
                    }
                }

                Thread.Sleep(5000);
            }
        }
        #endregion

        static Screen[] allScreen;
        static StreamerList streamerList;
        // [STAThread]
        [MTAThread]
        static void Main() {
            // Catchall for all uncaught exceptions !!
            AppDomain.CurrentDomain.UnhandledException += delegate(object sender, UnhandledExceptionEventArgs args) {
                // Application.Restart();

                var exception = (Exception)args.ExceptionObject;
                MessageBox.Show("FATAL: Unhandled - " + exception.Message, "ERROR", MessageBoxButtons.OK, MessageBoxIcon.Error);
                Environment.Exit(1);
            };

            try {
                TXTrecords.Add("machineName", System.Environment.MachineName);
                TXTrecords.Add("osVersion", System.Environment.OSVersion.VersionString);
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
                allScreen = Screen.AllScreens;
                for (int i=0; i < allScreen.Length; i++)
                    TXTrecords.Add("screen" + i,  allScreen[i].Bounds.X + "x" + allScreen[i].Bounds.Y + "x" + allScreen[i].Bounds.Width + "x" + allScreen[i].Bounds.Height);

                // TXTrecords.Add("screenWidth", System.Windows.Forms.Screen.PrimaryScreen.Bounds.Width.ToString());
                // TXTrecords.Add("screenHeight", System.Windows.Forms.Screen.PrimaryScreen.Bounds.Height.ToString());

                TcpListener ctrlAddr = new TcpListener(IPAddress.Any,  0 );
                ctrlAddr.Start();

                IPEndPoint sep = (IPEndPoint)ctrlAddr.LocalEndpoint;
                Debug.Assert(sep.Port != 0);

                String id, nm;
                using (RegistryKey dcs = Registry.CurrentUser.CreateSubKey("Software").CreateSubKey("FXPAL").CreateSubKey("DisplayCast").CreateSubKey("Player")) {
                    if (dcs.GetValue("uid") == null)
                        dcs.SetValue("uid", System.Guid.NewGuid().ToString("D"));
                    id = dcs.GetValue("uid").ToString();

                    if (dcs.GetValue("name") == null)
                        dcs.SetValue("name", System.Environment.UserName);
                    nm = dcs.GetValue("Name").ToString();
                    TXTrecords.Remove("name");
                    TXTrecords.Add("name", nm);
                }

                try {
                    nsPublisher = new NetService(Shared.DisplayCastGlobals.BONJOURDOMAIN, Shared.DisplayCastGlobals.PLAYER, id, sep.Port);
                    nsPublisher.DidPublishService += new NetService.ServicePublished(publishService_DidPublishService);
                    nsPublisher.DidNotPublishService += new NetService.ServiceNotPublished(publishService_DidNotPublishService);
                    nsPublisher.TXTRecordData = NetService.DataFromTXTRecordDictionary(TXTrecords);
                    nsPublisher.AllowApplicationForms = true;
                    nsPublisher.Publish();
                } catch {
                    MessageBox.Show("Apple Bonjour not installed. Pick up a local copy from https://developer.apple.com/opensource/", "FATAL", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    Environment.Exit(1);
                }
                
                // PlayerRemoteControl rc = new PlayerRemoteControl(ctrlAddr);
                // Thread ctrlThread = new Thread(new ThreadStart(rc.process));
                // ctrlThread.Name = "processNewControl";
                // ctrlThread.Start();

                ctrlAddr.BeginAcceptTcpClient(ctrlBeginAcceptTcpClient, ctrlAddr);

                MSE = new QueryMSE();
                myMac = getWIFIMACAddress();
                // if (myMac == null)
                //    myMac = displaycast;

                if (myMac != null) {
                    using (RegistryKey dcs = Registry.CurrentUser.CreateSubKey("Software").CreateSubKey("FXPAL").CreateSubKey("DisplayCast").CreateSubKey("Player")) {
                        discloseLocation = Convert.ToBoolean(dcs.GetValue("discloseLocation", false));
                    }

                    locThread = new Thread(new ThreadStart(monitorMyLocation));
                    locThread.Start();
                }

            } catch (Exception e) {
                MessageBox.Show("FATAL: Unhandled - " + e.StackTrace ,
                   "ERROR", MessageBoxButtons.OK, MessageBoxIcon.Error);
                Environment.Exit(1);
            }
            try {
                streamerList = new StreamerList();
                Application.Run(streamerList);
            } catch (Exception e) {
                MessageBox.Show("FATAL: Cannot start streamerList - " + e.StackTrace);
                Environment.Exit(1);
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="disposing"></param>
        protected override void Dispose(bool disposing) {
            if (disposing) {
                if (components != null)
                    components.Dispose();
                if (nsBrowser != null)
                    nsBrowser.Stop();
            }
            base.Dispose(disposing);

            Environment.Exit(1);
        }

        #region Remote control processing
        /// <summary>
        /// 
        /// </summary>
        private class cmdArgs {
            public TcpClient clnt;
            public String streamer;
            public String args = null;

            /// <summary>
            /// 
            /// </summary>
            /// <param name="clnt"></param>
            /// <param name="streamer"></param>
            public cmdArgs(TcpClient clnt, String streamer) {
                this.clnt = clnt;
                this.streamer = streamer;
            }

            /// <summary>
            /// 
            /// </summary>
            /// <param name="clnt"></param>
            /// <param name="streamer"></param>
            /// <param name="args"></param>
            public cmdArgs(TcpClient clnt, String streamer, String args) {
                this.clnt = clnt;
                this.streamer = streamer;
                this.args = args;
            }
        }

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

            strm.ReadTimeout = 5000;
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
                case "SHOW": {
                        cmdArgs args = new cmdArgs(clnt, words[1], words[2]);
                        object[] pList = { args, System.EventArgs.Empty };
#if PLAYER_TASKBAR
                        streamerList.BeginInvoke(new System.EventHandler(createUI), pList);
#else
                 browserList.BeginInvoke(new System.EventHandler(createUI), pList);
#endif
                    }
                    return;

                case "CLOSE": {
                        cmdArgs args = new cmdArgs(clnt, words[1]);
                        object[] pList = { args, System.EventArgs.Empty };
#if PLAYER_TASKBAR
                        streamerList.BeginInvoke(new System.EventHandler(closeUI), pList);
#else
                browserList.BeginInvoke(new System.EventHandler(closeUI), pList);
#endif
                    }
                    return;

                case "CLOSEALL": {
                        cmdArgs args = new cmdArgs(clnt, "ALL");
                        object[] pList = { args, System.EventArgs.Empty };
#if PLAYER_TASKBAR
                        streamerList.BeginInvoke(new System.EventHandler(closeUI), pList);
#else
                browserList.BeginInvoke(new System.EventHandler(closeUI), pList);
#endif
                    }
                    return;

                case "ICON": {
                        cmdArgs args = new cmdArgs(clnt, words[1], "ICON");
                        object[] pList = { args, System.EventArgs.Empty };
#if PLAYER_TASKBAR
                        streamerList.BeginInvoke(new System.EventHandler(iconifyUI), pList);
#else
                browserList.BeginInvoke(new System.EventHandler(iconifyUI), pList);
#endif
                    }
                    return;

                case "DICO": {
                        cmdArgs args = new cmdArgs(clnt, words[1], "DICO");
                        object[] pList = { args, System.EventArgs.Empty };
#if PLAYER_TASKBAR
                        streamerList.BeginInvoke(new System.EventHandler(iconifyUI), pList);
#else
                browserList.BeginInvoke(new System.EventHandler(iconifyUI), pList);
#endif
                    }
                    return;

                case "MOVE": {
                        cmdArgs args = new cmdArgs(clnt, words[1], words[2]);
                        object[] pList = { args, System.EventArgs.Empty };
#if PLAYER_TASKBAR
                        streamerList.BeginInvoke(new System.EventHandler(moveUI), pList);
#else    
                browserList.BeginInvoke(new System.EventHandler(moveUI), pList);
#endif
                    }
                    return;

                default:
                    bytes = Encoding.ASCII.GetBytes(DisplayCastGlobals.PLAYER_CMD_SYNTAX_ERROR);
                    strm.Write(bytes, 0, bytes.Length);
                    strm.Close();
                    return;
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="o"></param>
        /// <param name="evt"></param>
        private static void moveUI(object o, System.EventArgs evt) {
            cmdArgs cmd = (cmdArgs)o;
            String retString = Shared.DisplayCastGlobals.PLAYER_CMD_SYNTAX_ERROR;
            NetworkStream strm = cmd.clnt.GetStream();

            char[] delimiters = { 'x' };
            String[] words = cmd.args.Split(delimiters);
            if (words.Length != 4)
                retString = Shared.DisplayCastGlobals.PLAYER_USAGE_MOVE + cmd.args;
            else {
                int x = Convert.ToInt32(words[0]), y = Convert.ToInt32(words[1]), w = Convert.ToInt32(words[2]), h = Convert.ToInt32(words[3]);
                Rectangle newLoc = new Rectangle(x, y, w, h);

#if !PLAYER_TASKBAR
                browserList.BeginUpdate();
#endif 
                foreach (Streamer item in streamers) {
                    String id = item.GetHashCode().ToString();

                    if (cmd.streamer.Equals(id)) {
                        item.DesktopBounds = newLoc;
                        item.Show();

                        retString = DisplayCastGlobals.PLAYER_CMD_SUCCESS;
                    }
                }
#if !PLAYER_TASKBAR
                browserList.EndUpdate();
#endif
            }

            byte[] bytes = Encoding.ASCII.GetBytes(retString);
            strm.Write(bytes, 0, bytes.Length);
            strm.Close();
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="o"></param>
        /// <param name="evt"></param>
        private static void iconifyUI(object o, System.EventArgs evt) {
            cmdArgs cmd = (cmdArgs)o;
            String retString = DisplayCastGlobals.PLAYER_CMD_SYNTAX_ERROR;
            NetworkStream strm = cmd.clnt.GetStream();
            Boolean iconify = cmd.args.Equals("ICON");

#if !PLAYER_TASKBAR
            browserList.BeginUpdate();
#endif
            foreach (Streamer item in streamers) {
                String id = item.GetHashCode().ToString();

                if (cmd.streamer.Equals(id)) {
                    if (iconify)
                        item.WindowState = FormWindowState.Minimized;
                    else
                        item.WindowState = FormWindowState.Normal;
                    retString = DisplayCastGlobals.PLAYER_CMD_SUCCESS;
                }
            }
#if !PLAYER_TASKBAR
            browserList.EndUpdate();
#endif

            byte[] bytes = Encoding.ASCII.GetBytes(retString);
            strm.Write(bytes, 0, bytes.Length);
            strm.Close();
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="o"></param>
        /// <param name="evt"></param>
        private static void closeUI(object o, System.EventArgs evt) {
            cmdArgs cmd = (cmdArgs)o;
            String retString = DisplayCastGlobals.PLAYER_CMD_SYNTAX_ERROR;
            NetworkStream strm = cmd.clnt.GetStream();
            Streamer toDelete = null;

#if !PLAYER_TASKBAR
            browserList.BeginUpdate();
#endif
            foreach (Streamer item in streamers) {
                String id = item.GetHashCode().ToString();

                // if (cmd.streamer.Equals("ALL") || cmd.streamer.Equals(id)) {
                if (cmd.streamer.StartsWith(id)) {
                    item.Close();
                    toDelete = item;

                    retString = DisplayCastGlobals.PLAYER_CMD_SUCCESS;
                }
            }
#if !PLAYER_TASKBAR
            browserList.EndUpdate();
#endif

            if (toDelete != null)
                streamers.Remove(toDelete);

            byte[] bytes = Encoding.ASCII.GetBytes(retString);
            strm.Write(bytes, 0, bytes.Length);
            strm.Close();
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="o"></param>
        /// <param name="evt"></param>
        private static void createUI(object o, System.EventArgs evt) {
            cmdArgs cmd = (cmdArgs)o;
            String retString = DisplayCastGlobals.PLAYER_CMD_SYNTAX_ERROR;
            NetworkStream strm = cmd.clnt.GetStream();
            Boolean fs = cmd.args.ToUpper().Equals("FULLSCREEN");

#if PLAYER_TASKBAR
            foreach (MenuItem item in streamersItem.MenuItems) {
#else
            browserList.BeginUpdate();
            foreach (ListViewItem item in browserList.Items) {
#endif
                NetService service = (NetService)item.Tag;

                if (service == null)
                    continue;

                if (service.Name.Equals(cmd.streamer)) {
                    retString = showService(service, fs);

                    break;
                }
            }
#if !PLAYER_TASKBAR
            browserList.EndUpdate();
#endif
            byte[] bytes = Encoding.ASCII.GetBytes(retString);
            strm.Write(bytes, 0, bytes.Length);
            strm.Close();
        }
        #endregion

        #region Event processing
#if !PLAYER_TASKBAR
        private void StreamerListLoaded(object sender, EventArgs e) {
            browserList.Items.Clear();

            nsBrowser = new NetServiceBrowser();
            nsBrowser.InvokeableObject = this;
            nsBrowser.DidFindService += new NetServiceBrowser.ServiceFound(nsBrowser_DidFindService);
            nsBrowser.DidRemoveService += new NetServiceBrowser.ServiceRemoved(nsBrowser_DidRemoveService);
            nsBrowser.AllowApplicationForms = true;
            nsBrowser.SearchForService(DisplayCastGlobals.STREAMER, DisplayCastGlobals.BONJOURDOMAIN);

            using (RegistryKey dcs = Registry.CurrentUser.CreateSubKey("Software").CreateSubKey("FXPAL").CreateSubKey("DisplayCast").CreateSubKey("Player")) {
                myName.Text = dcs.GetValue("Name").ToString();
            }

            if (myMac == null)
                locationDisclose.Enabled = false;
            else
                locationDisclose.Checked = discloseLocation;
            /*
            if (locationDisclose.Checked) {
                object[] pList = { "ALL", System.EventArgs.Empty };
                browserList.BeginInvoke(new System.EventHandler(monitorMyLocation), pList);
            }
             */
        }

        private void browserList_ItemMouseHover(object sender, ListViewItemMouseHoverEventArgs e) {
            var item = e.Item;
            // NetService service = item.Tag;

            Trace.WriteLine("DEBUG: Hovering over " + item);
        }

         private void browserList_SelectedIndexChanged(object sender, System.EventArgs e) {
            ListView.SelectedListViewItemCollection slvic = browserList.SelectedItems;
            NetService service;

            if (slvic.Count > 0) {
                service = (NetService)slvic[0].Tag;
                slvic[0].Selected = false;
                showService(service, false);
            }
        }
#endif

        static int scrnNum = -1;
        /// <summary>
        /// 
        /// </summary>
        /// <param name="service"></param>
        /// <param name="fs"></param>
        /// <returns></returns>
        private static String showService(NetService service, Boolean fs) {
            IList addresses = service.Addresses;
            String streamId = DisplayCastGlobals.PLAYER_CMD_SYNTAX_ERROR;

            if (scrnNum < 0) {
                if (allScreen.Length > 1)
                    scrnNum = 1;
                else
                    scrnNum = 0;
            }

            Trace.WriteLine("DEBUG: Trying to connect to " + service.Name);
            foreach (System.Net.IPEndPoint addr in addresses) {
                System.Net.Sockets.TcpClient clntSocket = new System.Net.Sockets.TcpClient();
                try {
                    clntSocket.Connect(addr);
                    if (clntSocket.Connected) {
                        Trace.WriteLine("DEBUG: Connected to " + service.Name);

                        Streamer s = new Streamer(service.Name, getName(service), clntSocket.GetStream(), fs, nsPublisher, TXTrecords, allScreen[scrnNum++].Bounds);
                        if (scrnNum == allScreen.Length)
                            scrnNum = 0;

                        s.Show();
                        streamId = s.GetHashCode().ToString();
                        streamers.Add(s);

                        break;
                    }
                } catch {
                }
            }

            return streamId;
        }

#if PLAYER_TASKBAR
        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void changeName(object sender, EventArgs e) {
            if (nsPublisher != null) {
                String defaultName;

                using (RegistryKey dcs = Registry.CurrentUser.CreateSubKey("Software").CreateSubKey("FXPAL").CreateSubKey("DisplayCast").CreateSubKey("Player")) {
                    defaultName = dcs.GetValue("Name", System.Environment.UserName).ToString();
                    String name = Interaction.InputBox("Enter our name. Remote streamers will use this name to project their screencast.", "FXPal DisplayCast Player", defaultName, 0, 0);

                    if (name.Length != 0) {
                        dcs.SetValue("Name", name);
                        // Trace.WriteLine("DEBUG - name is " + name + " length is " + name.Length);

                        TXTrecords.Remove("name");
                        TXTrecords.Add("name", name);
                        nsPublisher.TXTRecordData = NetService.DataFromTXTRecordDictionary(TXTrecords);
                    }
                }
            }
        }
#else
        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void myName_TextChanged(object sender, EventArgs e) {
            using (RegistryKey dcs = Registry.CurrentUser.CreateSubKey("Software").CreateSubKey("FXPAL").CreateSubKey("DisplayCast").CreateSubKey("Player")) {
                dcs.SetValue("Name", myName.Text);
                TXTrecords.Remove("name");
                TXTrecords.Add("name", myName.Text);
                if (nsPublisher != null)
                    nsPublisher.TXTRecordData = NetService.DataFromTXTRecordDictionary(TXTrecords);
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void locationDisclose_CheckedChanged(object sender, EventArgs e) {
            discloseLocation = locationDisclose.Checked;

            using (RegistryKey dcs = Registry.CurrentUser.CreateSubKey("Software").CreateSubKey("FXPAL").CreateSubKey("DisplayCast").CreateSubKey("Player")) {
                dcs.SetValue("discloseLocation", discloseLocation);
            }
        }
#endif
        #endregion


        #region Utility functions
        /// <summary>
        /// 
        /// </summary>
        /// <param name="service"></param>
        /// <returns></returns>
        private static String getName(NetService service) {
            String name = service.Name;

            if (service.TXTRecordData != null) {
                byte[] txt = service.TXTRecordData;
                IDictionary dict = NetService.DictionaryFromTXTRecordData(txt);

                if (dict != null) {
                    foreach (DictionaryEntry kvp in dict) {
                        String key = (String)kvp.Key;

                        key = key.ToUpper();
                        if (key.Equals("NAME")) {
                            byte[] value = (byte[])kvp.Value;
                            try {
                                name = Encoding.UTF8.GetString(value);
                            } catch {
                            }
                            break;
                        }
                    }
                }
            }

            return name;
        }
        #endregion

        #region Bonjour publish code
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
            MessageBox.Show(String.Format("A DNSServiceException occured: {0}", exception.Message), "Error!", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        /// <summary>
        /// 
        /// </summary>
        private void publishService_StopPublish() {
            if (nsPublisher != null) {
                nsPublisher.Stop();
            }
        }
        #endregion

        #region Bonjour Browser code
        /// <summary>
        /// 
        /// </summary>
        /// <param name="browser"></param>
        /// <param name="service"></param>
        /// <param name="moreComing"></param>
        void nsBrowser_DidRemoveService(NetServiceBrowser browser, NetService service, bool moreComing) {
            ArrayList itemsToRemove = new ArrayList();

#if PLAYER_TASKBAR
            foreach (MenuItem item in streamersItem.MenuItems) {
                if (item.Tag == service)
                    itemsToRemove.Add(item);
            }

            foreach (MenuItem item in itemsToRemove) {
                streamersItem.MenuItems.Remove(item);

                while (true) {
                    Streamer toDelete = null;

                    foreach (Streamer strm in streamers) {
                        if (strm.id.Equals(service.Name)) {
                            strm.Close();
                            toDelete = strm;
                            break;
                        }
                    }

                    if (toDelete != null)
                        streamers.Remove(toDelete);
                    else
                        break;
                }
            }

            itemsToRemove.Clear();
#else
            browserList.BeginUpdate();
            
            foreach (ListViewItem item in browserList.Items) {
                if (item.Tag == service)
                    itemsToRemove.Add(item);
            }
            
            foreach (ListViewItem item in itemsToRemove) {
                browserList.Items.Remove(item);

                while (true) {
                    Streamer toDelete = null;

                    foreach (Streamer strm in streamers) {
                        if (strm.id.Equals(service.Name)) {
                            strm.Close();
                            toDelete = strm;
                            break;
                        }
                    }

                    if (toDelete != null)
                        streamers.Remove(toDelete);
                    else
                        break;
                }
            }

            itemsToRemove.Clear();
            browserList.EndUpdate();
#endif
            service.Dispose();
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="browser"></param>
        /// <param name="service"></param>
        /// <param name="moreComing"></param>
        void nsBrowser_DidFindService(NetServiceBrowser browser, NetService service, bool moreComing) {
            service.DidResolveService += new NetService.ServiceResolved(ns_DidResolveService);

            Trace.WriteLine("DEBUG: Found - " + service.Name + ". Resolving...");
            service.ResolveWithTimeout(5);
         }

#if PLAYER_TASKBAR
        /// <summary>
        /// 
        /// </summary>
        /// <param name="Sender"></param>
        /// <param name="e"></param>
        private void selectStreamer(object Sender, EventArgs e) {
            MenuItem clicked = (MenuItem)Sender;
            NetService service = (NetService)clicked.Tag;
            if (service == null)
                return;

            if (clicked.Checked) {
                Streamer toDelete = null;
               
                foreach (Streamer item in streamers) {
                    String id = item.GetHashCode().ToString();

                    // if (cmd.streamer.Equals("ALL") || cmd.streamer.Equals(id)) {
                    if (service.Name.Equals(item.id)) {
                        item.Close();
                        toDelete = item;
                    }
                }

                if (toDelete != null)
                    streamers.Remove(toDelete);

                clicked.Checked = false;
            } else {
                clicked.Checked = true;
                showService(service, false);
            }
        }
#endif

        /// <summary>
        /// 
        /// </summary>
        /// <param name="service"></param>
        public void ns_DidResolveService(NetService service) {
            service.DidUpdateTXT += new NetService.ServiceTXTUpdated(ns_DidUpdateTXT);
            service.StartMonitoring();

            Trace.WriteLine("DEBUG: Resolved - " + service.Name);
            // Use the GUID as my name unless TXT has a better name
            ListViewItem item = new ListViewItem(getName(service));
            item.Tag = service;

#if PLAYER_TASKBAR
            MenuItem newElem = new System.Windows.Forms.MenuItem();
            newElem.Text = getName(service);
            newElem.Click += new System.EventHandler(selectStreamer);
            newElem.Select += new System.EventHandler(selectStreamer);
            newElem.Tag = service;

// #if PLAYER_USE_STREAMERICON
         
// #endif

            streamersItem.MenuItems.Add(newElem);
#else
            browserList.BeginUpdate();
            browserList.Items.Add(item);
            browserList.EndUpdate();
#endif
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="service"></param>
        void ns_DidUpdateTXT(NetService service) {
#if PLAYER_TASKBAR
            foreach (MenuItem item in streamersItem.MenuItems) {
                NetService iService = (NetService)item.Tag;

                if (iService == null)
                    continue;

                if (iService.Name.Equals(service.Name))
                    item.Text = getName(service);
            }
#else
            browserList.BeginUpdate();
            foreach (ListViewItem item in browserList.Items) {
                NetService iService = (NetService)item.Tag;

                if (iService.Name.Equals(service.Name))
                    item.Text = getName(service);
            }
            browserList.EndUpdate();
            // MessageBox.Show("Did update TXT + " + service.Name, "DEBUG");
#endif
        }
        #endregion

#if PLAYER_TASKBAR
        /// <summary>
        /// 
        /// </summary>
        private void InitializeComponent() {
            this.SuspendLayout();
            // 
            // StreamerList
            // 
            this.ClientSize = new System.Drawing.Size(292, 273);
            this.Name = "StreamerList";
            this.WindowState = System.Windows.Forms.FormWindowState.Minimized;
            this.ResumeLayout(false);

        }
#else
		#region Windows Form Designer generated code
		/// <summary>
		/// Required method for Designer support - do not modify
		/// the contents of this method with the code editor.
		/// </summary>
		private void InitializeComponent() {
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(StreamerList));
            browserList = new System.Windows.Forms.ListView();
            this.nameLabel = new System.Windows.Forms.Label();
            this.myName = new System.Windows.Forms.TextBox();
            this.locationDisclose = new System.Windows.Forms.CheckBox();
            this.SuspendLayout();
            // 
            // browserList
            // 
            browserList.GridLines = true;
            browserList.Location = new System.Drawing.Point(3, 25);
            browserList.MultiSelect = false;
            browserList.Name = "browserList";
            browserList.Size = new System.Drawing.Size(275, 163);
            browserList.Sorting = System.Windows.Forms.SortOrder.Ascending;
            browserList.TabIndex = 1;
            browserList.UseCompatibleStateImageBehavior = false;
            browserList.View = System.Windows.Forms.View.List;
            browserList.ItemMouseHover += new System.Windows.Forms.ListViewItemMouseHoverEventHandler(this.browserList_ItemMouseHover);
            browserList.SelectedIndexChanged += new System.EventHandler(this.browserList_SelectedIndexChanged);
            // 
            // nameLabel
            // 
            this.nameLabel.AutoSize = true;
            this.nameLabel.Font = new System.Drawing.Font("Microsoft Sans Serif", 8.25F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.nameLabel.Location = new System.Drawing.Point(12, 9);
            this.nameLabel.Name = "nameLabel";
            this.nameLabel.Size = new System.Drawing.Size(57, 13);
            this.nameLabel.TabIndex = 2;
            this.nameLabel.Text = "My name";
            // 
            // myName
            // 
            this.myName.AcceptsReturn = true;
            this.myName.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.myName.Font = new System.Drawing.Font("Microsoft Sans Serif", 8.25F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.myName.Location = new System.Drawing.Point(68, 2);
            this.myName.MaxLength = 64;
            this.myName.Name = "myName";
            this.myName.Size = new System.Drawing.Size(210, 20);
            this.myName.TabIndex = 3;
            this.myName.TextChanged += new System.EventHandler(this.myName_TextChanged);
            // 
            // locationDisclose
            // 
            this.locationDisclose.AutoSize = true;
            this.locationDisclose.CheckAlign = System.Drawing.ContentAlignment.MiddleRight;
            this.locationDisclose.Font = new System.Drawing.Font("Microsoft Sans Serif", 8.25F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.locationDisclose.Location = new System.Drawing.Point(68, 194);
            this.locationDisclose.Name = "locationDisclose";
            this.locationDisclose.Size = new System.Drawing.Size(130, 17);
            this.locationDisclose.TabIndex = 7;
            this.locationDisclose.Text = "Disclose location?";
            this.locationDisclose.UseVisualStyleBackColor = true;
            this.locationDisclose.CheckedChanged += new System.EventHandler(this.locationDisclose_CheckedChanged);
            // 
            // StreamerList
            // 
            this.AutoScaleBaseSize = new System.Drawing.Size(5, 13);
            this.AutoScroll = true;
            this.AutoSize = true;
            this.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
            this.BackColor = System.Drawing.SystemColors.Window;
            this.ClientSize = new System.Drawing.Size(281, 215);
            this.Controls.Add(this.locationDisclose);
            this.Controls.Add(this.myName);
            this.Controls.Add(this.nameLabel);
            this.Controls.Add(browserList);
            this.Cursor = System.Windows.Forms.Cursors.Default;
            this.Icon = ((System.Drawing.Icon)(resources.GetObject("$this.Icon")));
            this.Name = "StreamerList";
            this.Text = "FXPal DIsplayCast Player";
            this.TopMost = true;
            this.Load += new System.EventHandler(this.StreamerListLoaded);
            this.ResumeLayout(false);
            this.PerformLayout();

		}
		#endregion
#endif
        /// <summary>
        /// 
        /// </summary>
        public StreamerList() {
            InitializeComponent();

#if PLAYER_TASKBAR
            this.ShowInTaskbar = false;

            streamersItem = new System.Windows.Forms.MenuItem();
            streamersItem.Text = "W&atch Streamer";
            streamersItem.MenuItems.Add("-");

            MSE = new QueryMSE();
            myMac = getWIFIMACAddress();
            /* if (myMac == null)
                myMac = displaycast; */
            if (myMac != null) {
                locationItem = new System.Windows.Forms.MenuItem();
                locationItem.Text = "Disclose location?";
                // locationItem.Index = 4;
                // playersItem.Index++;
                // archiversItem.Index++;
                locationItem.Click += new System.EventHandler(locationChecked);
                using (RegistryKey dcs = Registry.CurrentUser.CreateSubKey("Software").CreateSubKey("FXPAL").CreateSubKey("DisplayCast").CreateSubKey("Streamer")) {
                    locationItem.Checked = Convert.ToBoolean(dcs.GetValue("discloseLocation"));
                }
                discloseLocation = locationItem.Checked;

                locThread = new Thread(new ThreadStart(monitorMyLocation));
                locThread.Start();
            }

            changeNameItem = new System.Windows.Forms.MenuItem();
            changeNameItem.Text = "C&hange Name";
            changeNameItem.Click += new System.EventHandler(changeName);

            aboutItem = new System.Windows.Forms.MenuItem();
            aboutItem.Text = "A&bout...";
            aboutItem.Click += new System.EventHandler(about);

            exitItem = new System.Windows.Forms.MenuItem();
            exitItem.Text = "E&xit";
            exitItem.Click += new System.EventHandler(exitItem_Click);

            contextMenu = new System.Windows.Forms.ContextMenu();

            contextMenu.MenuItems.Add(streamersItem);
            contextMenu.MenuItems.Add("-");

            if (locationItem != null)
                contextMenu.MenuItems.Add(locationItem);

            contextMenu.MenuItems.Add(changeNameItem);

            contextMenu.MenuItems.Add("-");
            contextMenu.MenuItems.Add(aboutItem);

            contextMenu.MenuItems.Add("-");
            contextMenu.MenuItems.Add(exitItem);

            iComponents = new System.ComponentModel.Container();
            notifyIcon = new NotifyIcon(iComponents);
            notifyIcon.Icon = new Icon("Player.ico");
            // notifyIcon.Icon = Streamer.Properties.Resources.Streamer;
            // notifyIcon.Icon = new Icon(System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("FXPAL.DisplayCast.Streamer.Streamer.ico"));

            notifyIcon.ContextMenu = contextMenu;
            notifyIcon.Text = "FXPAL Displaycast Player";
            notifyIcon.Visible = true;
            notifyIcon.DoubleClick += new System.EventHandler(this.notifyIcon_DoubleClick);

            nsBrowser = new NetServiceBrowser();
            nsBrowser.InvokeableObject = this;
            nsBrowser.DidFindService += new NetServiceBrowser.ServiceFound(nsBrowser_DidFindService);
            nsBrowser.DidRemoveService += new NetServiceBrowser.ServiceRemoved(nsBrowser_DidRemoveService);
            nsBrowser.AllowApplicationForms = true;
            nsBrowser.SearchForService(Shared.DisplayCastGlobals.STREAMER, Shared.DisplayCastGlobals.BONJOURDOMAIN);
#endif
        }
    }
}
