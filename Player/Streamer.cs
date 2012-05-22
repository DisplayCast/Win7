// Copyright (c) 2012, Fuji Xerox Co., Ltd.
// All rights reserved.
// Author: Surendar Chandra, FX Palo Alto Laboratory, Inc.

using System;
using System.Drawing;
using System.Windows.Forms;
using System.IO;
using System.Net.Sockets;
using System.Drawing.Imaging;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using System.Collections;
using ZeroconfService;

using Shared;

#if USE_IONIC_ZLIB
using Ionic.Zlib;
using Ionic.Crc;
#else
using System.IO.Compression;
#endif

namespace FXPAL.DisplayCast.Player {
    /// <summary>
    /// Performs the work of showing data from a streamer
    /// </summary>
    public partial class Streamer : Form {
        public String id;                   // The GUID that is being watched is public so others can check what we are watching

        [DllImport("user32.dll", EntryPoint = "GetSystemMetrics")]
        static extern int GetSystemMetrics(int which);

        [DllImport("user32.dll")]
        static extern void SetWindowPos(IntPtr hwnd, IntPtr hwndInsertAfter, int X, int Y, int width, int height, uint flags);

        private const int SM_CXSCREEN = 0;
        private const int SM_CYSCREEN = 1;
        private static IntPtr HWND_TOP = IntPtr.Zero;
        private const int SWP_SHOWWINDOW = 64; // 0×0040

        private NetworkStream clntStream = null;    // Stream that is being displayed

        // Screen and update parameters are sent with each update
        private Int32 width, height;
        private Int32 maskX, maskY, maskWidth, maskHeight;

        private Boolean windowSized = false;        // Keep track of whether we have updated the window size to reflect Streamer

        // private String name;                        // We are watching this stream
        private Boolean fs = false;                 // Fullscreen?
        private Hashtable publisherTXTrecords;               // To manage session information. When we quite, we remove ourselves and then report
        private NetService nsPublisher;             // Publish information about sessions
        private Rectangle bounds = Rectangle.Empty;                   // Multiscreen support

        // Buffer to store image data - all new update rectangles operate on this buffer
        private byte[] imageBuf = null;

        // Used to check whether the MASK value changed
        private int prevMX = -1, prevMY = -1, prevMW = -1, prevMH = -1;

        /// <summary>
        /// Read and process the next update. The first four bytes represent the length of the subsequent update
        /// </summary>
        private void processUpdate() {
            byte[] szBuf = new byte[4];

            // First read size of packet - only applicable for TCP
            int read = 0;
            while (read < 4) {
                try {
                    read += clntStream.Read(szBuf, read, 4 - read);
                } catch (IOException ioe) {
                    // MessageBox.Show("DEBUG: DisplayCast stream closed- " + ioe.Message, "INFO");
                    Trace.WriteLine("DEBUG: IO error - " + ioe.Message);
            
                    clntStream.Close();
                    return;
                }
            }

            int sz = System.BitConverter.ToInt32(szBuf, 0);

            // Now ignore the first two bytes [CompressionMethodandFlag] and [Flag], DeFlateStream does not need them
            // Also ignore the last 4 bytes (Adler32 checksum)
            try {
                clntStream.ReadByte();
                clntStream.ReadByte();
            } catch (IOException ioe) {
                Trace.WriteLine("DEBUG: DisplayCast stream closed. " + ioe.Message, "INFO");

                clntStream.Close();
                return;
            }
            sz -= (2);
            Debug.Assert(sz > 0);

            byte[] buf = new byte[sz];
            read = 0;
            while (read < sz) {
                try {
                    read += clntStream.Read(buf, read, sz - read);
                } catch (IOException ioe) {
                   Trace.WriteLine("DEBUG: DisplayCast stream closed: " + ioe.Message, "INFO");

                    clntStream.Close();
                    return;
                }
            }
            DecompressDisplay(buf);
        }

        /// <summary>
        /// Decompress and display the update contents
        /// </summary>
        /// <param name="data">data bytes</param>
        private void DecompressDisplay(byte[] data) {
            var compressedStream = new MemoryStream(data);
            Int32 x, y, w, h;

            using (MemoryStream clearStream = new MemoryStream()) {
                using (DeflateStream zipStream = new DeflateStream(compressedStream, CompressionMode.Decompress)) {
                    byte[] buffer = new byte[4096];
                    byte[] szBuf = new byte[4];
                    int readAmt;

                    readAmt = zipStream.Read(szBuf, 0, 4);
                    Debug.Assert(readAmt == 4);
                    UInt32 hdr = System.BitConverter.ToUInt32(szBuf, 0);
                    hdr = (UInt32)System.Net.IPAddress.NetworkToHostOrder((Int32)hdr);
                    width = (Int32)(hdr >> 16);
                    height = (Int32)(hdr & 0xFFFF);

                    readAmt = zipStream.Read(szBuf, 0, 4);
                    Debug.Assert(readAmt == 4);
                    hdr = System.BitConverter.ToUInt32(szBuf, 0);
                    hdr = (UInt32)System.Net.IPAddress.NetworkToHostOrder((Int32)hdr);
                    maskX = (Int32)(hdr >> 16);
                    maskY = (Int32)(hdr & 0xFFFF);

                    readAmt = zipStream.Read(szBuf, 0, 4);
                    Debug.Assert(readAmt == 4);
                    hdr = System.BitConverter.ToUInt32(szBuf, 0);
                    hdr = (UInt32)System.Net.IPAddress.NetworkToHostOrder((Int32)hdr);
                    maskWidth = (Int32)(hdr >> 16);
                    maskHeight = (Int32)(hdr & 0xFFFF);

                    if (!((prevMX == maskX) && (prevMY == maskY) && (prevMW == maskWidth) && (prevMH == maskHeight))) {
                        DisplayMask(maskX, maskY, maskWidth, maskHeight, width, height);
                        
                        prevMX = maskX;
                        prevMY = maskY;
                        prevMW = maskWidth;
                        prevMH = maskHeight;
                    }

                    readAmt = zipStream.Read(szBuf, 0, 4);
                    Debug.Assert(readAmt == 4);
                    hdr = System.BitConverter.ToUInt32(szBuf, 0);
                    hdr = (UInt32)System.Net.IPAddress.NetworkToHostOrder((Int32)hdr);
                    x = (Int32)(hdr >> 16);
                    y = (Int32)(hdr & 0xFFFF);

                    readAmt = zipStream.Read(szBuf, 0, 4);
                    Debug.Assert(readAmt == 4);
                    hdr = System.BitConverter.ToUInt32(szBuf, 0);
                    hdr = (UInt32)System.Net.IPAddress.NetworkToHostOrder((Int32)hdr);
                    w = (Int32)(hdr >> 16);
                    h = (Int32)(hdr & 0xFFFF);

                    int read = 0;
                    while (true) {
                        try {
                            read = zipStream.Read(buffer, 0, buffer.Length);
                        } catch (Exception e) {
                            // Trace.WriteLine("{0} Error code: {}.", e.Message, e.ErrorCode);
                            MessageBox.Show("Message: " + e.Message, "FATAL");
                        }
                        if (read > 0)
                            clearStream.Write(buffer, 0, read);
                        else
                            break;
                    }
                    zipStream.Close();
                }

                DisplayUpdate(x, y, w, h, clearStream.ToArray());
            }
        }

        /// <summary>
        /// Display the uncompressed bitmap data
        /// </summary>
        /// <param name="x">location of the update</param>
        /// <param name="y"></param>
        /// <param name="w"></param>
        /// <param name="h"></param>
        /// <param name="bitmap">bitmapdata</param>
        private void DisplayUpdate(Int32 x, Int32 y, Int32 w, Int32 h, byte[] bitmap) {
            if (imageBuf == null)
                imageBuf = new byte[width * height * 4];
            Debug.Assert(imageBuf.Length == (width * height * 4));

            int indxb = 0;
            int srcStart = w * h;
            for (int j = 0; j < h; j++) {
                for (int i = 0; i < w; i++) {
                    int indx = (width * (y + j) + x + i) * 4;

#if USE_BITMAP_COMPRESS
                    if (bitmap[indxb] == 0xFF) {
                        imageBuf[indx] = bitmap[srcStart++];
                        imageBuf[indx + 1] = bitmap[srcStart++];
                        imageBuf[indx + 2] = bitmap[srcStart++];
                        imageBuf[indx + 3] = 0xFF;
                    }
                    indxb++;
#else
                    if (bitmap[indxb + 3] > 0) {
                        imageBuf[indx] = bitmap[indxb++];
                        imageBuf[indx + 1] = bitmap[indxb++];
                        imageBuf[indx + 2] = bitmap[indxb++];
                        imageBuf[indx + 3] = 255;
                        indxb++;
                    } else
                        indxb += 4;
#endif
                }
            }

            // bitmap[counter] = 0; // Blue
            // bitmap[counter + 1] = 0; // Green
            // bitmap[counter + 2] = 255; // Red
            // bitmap[counter + 3] = 0; // Alpha
            if (!IsDisposed) {
                try {
                    if (streamImage != null) {
                        object[] pList = { this, System.EventArgs.Empty };
                        streamImage.BeginInvoke(new System.EventHandler(updateUI), pList);
                    }
                } catch {
                    // WTF. I get this thrown when the Player is auto-started on reboot. I am not sure why. Just catching anyway
                }
            }
        }

        /// <summary>
        /// Update the cached frame buffer data
        /// </summary>
        /// <param name="mx"></param>
        /// <param name="my"></param>
        /// <param name="mw"></param>
        /// <param name="mh"></param>
        /// <param name="w"></param>
        /// <param name="h"></param>
        private void DisplayMask(Int32 mx, Int32 my, Int32 mw, Int32 mh, Int32 w, Int32 h) {
            if (imageBuf == null)
                return;

            int indxb = 0;
            for (int j = 0; j < h; j++) {
                for (int i = 0; i < w; i++) {
                    imageBuf[indxb + 3] = 0x00;
                    indxb += 4;
                }
            }
            
            for (int j = 0; j < mh; j++) {
                for (int i = 0; i < mw; i++) {
                    int indx = (w * (my + j) + mx + i) * 4;

                    imageBuf[indx + 3] = 0xFF;
                }
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="o"></param>
        /// <param name="evt"></param>
        private void updateUI(object o, System.EventArgs evt) {
            if (windowSized == false) {
                this.streamImage.Width = width;
                this.streamImage.Height = height;

                if (fs) {
                    this.FormBorderStyle = FormBorderStyle.None;
                    this.WindowState = FormWindowState.Maximized;
                    this.TopMost = true;
                    // this.AutoSize = true;

                    if (bounds.IsEmpty) {
                        int w = GetSystemMetrics(SM_CXSCREEN), h = GetSystemMetrics(SM_CYSCREEN);
                        this.Size = new Size(w, h);
                        SetWindowPos(this.Handle, HWND_TOP, 0, 0, w, h, SWP_SHOWWINDOW);
                    } else {
                        this.Size = new Size(bounds.Width, bounds.Height);
                        SetWindowPos(this.Handle, HWND_TOP, bounds.X, bounds.Y, bounds.Width, bounds.Height, SWP_SHOWWINDOW);
                    }
                } else {
                    this.Width = width;
                    this.Height = height;

                    this.WindowState = FormWindowState.Normal;
                }
                windowSized = true;
                this.Show();
            }

            try {
                Bitmap bmp = CopyDataToBitmap(width, height, imageBuf);
                this.streamImage.Image = bmp;
                // this.streamImage.Enabled = true;
                this.streamImage.Update();

                // this.streamImage.CreateGraphics().DrawRectangle(System.Drawing.SystemPens.Highlight, 0, 0, 100, 100);
                //  this.streamImage.CreateGraphics().DrawImage(bmp, w, h);
                // this.streamImage.BackColor = SystemColors.ControlText;
                // this.streamImage.Show();
                // MessageBox.Show("I am there " + x +"x" + y + " " + w + "x" + h, "INFO");
            } catch (Exception e) {
                MessageBox.Show("Could not load backgroundimage " + e.Message, "INFO");
            }

            if (!IsDisposed) {
                MethodInvoker mi = new MethodInvoker(processUpdate);
                mi.BeginInvoke(null, null);
            }
        }

        /// <summary>
        /// function CopyDataToBitmap
        /// Purpose: Given the pixel data return a bitmap of size [352,288],PixelFormat=24RGB 
        /// </summary>
        /// <param name="data">Byte array with pixel data</param>
        private Bitmap CopyDataToBitmap(Int32 width, Int32 height, byte[] data) {
            //Here create the Bitmap to the know height, width and format
            Bitmap bmp = new Bitmap(width, height, PixelFormat.Format32bppArgb /* PixelFormat.Format32bppRgb */);

            //Create a BitmapData and Lock all pixels to be written 
            BitmapData bmpData = bmp.LockBits(
                                 new Rectangle(0, 0, bmp.Width, bmp.Height),
                                 ImageLockMode.WriteOnly, bmp.PixelFormat);

            //Copy the data from the byte array into BitmapData.Scan0
            System.Runtime.InteropServices.Marshal.Copy(data, 0, bmpData.Scan0, data.Length);

            //Unlock the pixels
            bmp.UnlockBits(bmpData);

            //Return the bitmap 
            return bmp;
        }
        
        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Streamer_Load(object sender, EventArgs e) {
            if (!IsDisposed) {
                MethodInvoker mi = new MethodInvoker(processUpdate);
                mi.BeginInvoke(null, null);
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Streamer_SizeLocationChanged(object sender, EventArgs e) {
            /* Control control = (Control)sender;
            String val;

            if ((control.Location.X == -32000) && (control.Location.Y == -32000))
                val = "-x-";
            else
                val = control.Location.X + "x" + control.Location.Y;
            val = val + "x" + control.Size.Width + "x" + control.Size.Height;
             */
            String val = null;
            using (RegistryKey dcs = Registry.CurrentUser.CreateSubKey("Software").CreateSubKey("FXPAL").CreateSubKey("DisplayCast").CreateSubKey("Player")) {
                if (dcs.GetValue("uid") == null)
                    dcs.SetValue("uid", System.Guid.NewGuid().ToString("D"));
                val = this.id + " " + dcs.GetValue("uid").ToString();
            }

            Rectangle loc = DesktopBounds;
            val = val + " " + loc.X + " " + loc.Y + " " + loc.Width + " " + loc.Height;
            val = val + " " + ((this.WindowState == FormWindowState.Minimized) ? "1" : "0");
            val = val + " " + ((fs) ? "1" : "0");

            String sid = this.GetHashCode().ToString();
            Trace.WriteLine("Resized to " + val);

            if (publisherTXTrecords != null) {
                publisherTXTrecords.Remove(sid);
                publisherTXTrecords.Add(sid, val);
   
                nsPublisher.TXTRecordData = NetService.DataFromTXTRecordDictionary(publisherTXTrecords);
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Streamer_Closed(object sender, FormClosedEventArgs e) {
            if (publisherTXTrecords != null) {
                String sid = this.GetHashCode().ToString();

                publisherTXTrecords.Remove(sid);
                nsPublisher.TXTRecordData = NetService.DataFromTXTRecordDictionary(publisherTXTrecords);
            }

            clntStream.Close();
            Dispose();
        }

        /// <summary>
        /// Minimal constructor
        /// </summary>
        /// <param name="id">The streamer that is being watched</param>
        /// <param name="clntStream">Network data stream</param>
        public Streamer(String id, NetworkStream clntStream) {
            InitializeComponent();

            this.id = this.Text = id;
            this.clntStream = clntStream;
            this.fs = true;
            this.nsPublisher = null;
            this.publisherTXTrecords = null;
            this.bounds = Rectangle.Empty;
        }

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="id">GUID of streamer</param>
        /// <param name="name">User configured name</param>
        /// <param name="clntStream">Network stream for getting streaming data</param>
        /// <param name="fs">Full Screen</param>
        /// <param name="nsPublisher">NetworkService to broadcasts information about our sessions</param>
        /// <param name="publisherTXTrecords">TXTrecords used by the publisher</param>
        /// <param name="bounds">In Multiscreen mode, used to restrict where we display this streamer</param>
        public Streamer(String id, String name, NetworkStream clntStream, Boolean fs, NetService nsPublisher, Hashtable publisherTXTrecords, Rectangle bounds) {
            InitializeComponent();

            this.id = id;
            this.Text = name;
            this.clntStream = clntStream;
            this.fs = fs;
            this.nsPublisher = nsPublisher;
            this.publisherTXTrecords = publisherTXTrecords;
            this.bounds = bounds;                    
        }
    }
}
