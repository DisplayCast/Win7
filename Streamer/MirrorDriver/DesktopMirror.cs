// Copyright (c) 2012, Fuji Xerox Co., Ltd.
// All rights reserved.
// Author: Surendar Chandra, FX Palo Alto Laboratory, Inc.

#define PRODUCTION

#if !PROFUCTION
#define DEBUG_EXP_1
#undef DEBUG_EXP_2
#undef DEBUG_EXP_3
#endif 

using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using System.Collections;
using System.IO;
using System.IO.Compression;
using System.Net;

using Microsoft.Win32;

using FXPAL.DisplayCast.Streamer;

// Mirror driver based on code from http://cellbank.googlecode.com/svn/MirrSharp/
namespace Mirror.Driver {
    /// <summary>
    /// Private class to keep track of pixmap boundaries
    /// </summary>
    public class DesktopChangeEventArgs : EventArgs {
        public int x;
        public int y;
        public int w;
        public int h;
        public OperationType type;

        public DesktopChangeEventArgs(int x1, int y1, int x2, int y2, OperationType type) {
            this.x = x1;
            this.y = y1;
            this.w = x2 - x1;
            this.h = y2 - y1;
            this.type = type;
        }
    }

	public class DesktopMirror : IDisposable {
        public event EventHandler<DesktopChangeEventArgs> DesktopChange;

        public Boolean loaded = false;
        public static int _bitmapWidth, _bitmapHeight, _bitmapBpp;
        public byte[] screen;

		#region External Constants
		private const int Map = 1030;
		private const int UnMap = 1031;
		private const int TestMapped = 1051;

		private const int IGNORE = 0;
		private const int BLIT = 12;
		private const int TEXTOUT = 18;
		private const int MOUSEPTR = 48;

		private const int CDS_UPDATEREGISTRY = 0x00000001;
		private const int CDS_TEST = 0x00000002;
		private const int CDS_FULLSCREEN = 0x00000004;
		private const int CDS_GLOBAL = 0x00000008;
		private const int CDS_SET_PRIMARY = 0x00000010;
		private const int CDS_RESET = 0x40000000;
		private const int CDS_SETRECT = 0x20000000;
		private const int CDS_NORESET = 0x10000000;
		private const int MAXIMUM_ALLOWED = 0x02000000;
		private const int DM_BITSPERPEL = 0x40000;
		private const int DM_PELSWIDTH = 0x80000;
		private const int DM_PELSHEIGHT = 0x100000;
		private const int DM_POSITION = 0x00000020;
		#endregion

		#region External Methods
        internal static class NativeMethods {
            [DllImport("user32.dll", CharSet = CharSet.Ansi, BestFitMapping = false, ThrowOnUnmappableChar = true)]
            public static extern int ChangeDisplaySettingsEx(string lpszDeviceName, ref DeviceMode mode, IntPtr hwnd, uint dwflags, IntPtr lParam);

            [DllImport("gdi32.dll", SetLastError = true, CharSet = CharSet.Ansi, BestFitMapping = false, ThrowOnUnmappableChar = true)]
            public static extern IntPtr CreateDC(string lpszDriver, string lpszDevice, string lpszOutput, IntPtr lpInitData);

            [DllImport("gdi32.dll")]
            public static extern bool DeleteDC(IntPtr pointer);

            [DllImport("user32.dll", CharSet=CharSet.Ansi, BestFitMapping=false, ThrowOnUnmappableChar=true)]
            public static extern bool EnumDisplayDevices(string lpDevice, uint ideviceIndex, ref DisplayDevice lpdevice, uint dwFlags);

            [DllImport("gdi32.dll", SetLastError = true)]
            public static extern int ExtEscape(IntPtr hdc, int nEscape, int cbInput, IntPtr lpszInData, int cbOutput, IntPtr lpszOutData);

            [DllImport("user32.dll", EntryPoint = "GetDC")]
            public static extern IntPtr GetDC(IntPtr ptr);

            [DllImport("user32.dll", EntryPoint = "ReleaseDC")]
            public static extern UInt32 ReleaseDC(IntPtr hWnd, IntPtr hDc);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="lpszDeviceName"></param>
        /// <param name="mode"></param>
        /// <param name="hwnd"></param>
        /// <param name="dwflags"></param>
        /// <param name="lParam"></param>
        private static void SafeChangeDisplaySettingsEx(string lpszDeviceName, ref DeviceMode mode, IntPtr hwnd, uint dwflags, IntPtr lParam) {
            int result = NativeMethods.ChangeDisplaySettingsEx(lpszDeviceName, ref mode, hwnd, dwflags, lParam);
            switch (result) {
                case 0:
                    return; //DISP_CHANGE_SUCCESSFUL
                case 1:
                    throw new Exception("The computer must be restarted for the graphics mode to work."); //DISP_CHANGE_RESTART
                case -1:
                    throw new Exception("The display driver failed the specified graphics mode."); // DISP_CHANGE_FAILED
                case -2:
                    throw new Exception("The graphics mode is not supported."); // DISP_CHANGE_BADMODE
                case -3:
                    throw new Exception("Unable to write settings to the registry."); // DISP_CHANGE_NOTUPDATED
                case -4:
                    throw new Exception("An invalid set of flags was passed in."); // DISP_CHANGE_BADFLAGS
                case -5:
                    throw new Exception("An invalid parameter was passed in. This can include an invalid flag or combination of flags."); // DISP_CHANGE_BADPARAM
                case -6:
                    throw new Exception("The settings change was unsuccessful because the system is DualView capable."); // DISP_CHANGE_BADDUALVIEW
            }
        }
		#endregion

        #region screen filling functions
        /// <summary>
        /// Fills our copy of framebuffer from actual framebuffer, only called when we suspect that they are out of sync with each other
        /// </summary>
        public void fillScreen() {
            // Get the initial screen for further incremental operations
            GetChangesBuffer getChangesBuffer = (GetChangesBuffer)Marshal.PtrToStructure(_getChangesBuffer, typeof(GetChangesBuffer));

            if (screen == null)
                screen = new byte[_bitmapWidth * _bitmapHeight * sizeof(UInt32)];
            
            try {
                Marshal.Copy(getChangesBuffer.UserBuffer, screen, 0, screen.Length);
            } catch (Exception e) {
                MessageBox.Show("FATAL: Failure to copy framebuffer data " + e.StackTrace);
                Environment.Exit(1);
            }
            /* Make sure that all pixels are opague. Unnecessary
                for (int i = 0; i < screen.Length; i += 4)
                    screen[i + 3] = 0xFF;
             */
        }

        /// <summary>
        /// Used by the controlAPI to get a snapshot. We act like a web server. 
        /// I assume that the snapshots are taken infrequently. Hopefully, this API will not be used at 30 fps 
        /// (because of the PNG compression overhead). Note that the overhead is onerous when taking SNAPshots frequently might make sense
        /// </summary>
        /// <param name="result"></param>
        public void sendScreenShot(IAsyncResult result) {
           // Take the request off the queue and prepare the webserver to accept further requests
            HttpListener listener = (HttpListener)result.AsyncState;
            HttpListenerContext context = listener.EndGetContext(result);
            HttpListenerRequest request = context.Request;

            String c = request.Url.LocalPath;
            int width = _bitmapWidth, height = _bitmapHeight;

            if (request.Url.Query.Length > 0) {
                String cmd = request.Url.Query.Substring(1).ToUpper();
                char[] delimiters = { '=', '&' };
                String[] words = cmd.Split(delimiters);

                // Specify width to scale the screen snapshot
                if (words.Length == 2) {
                    if ((words[0].Equals("WIDTH"))) {
                        width = Convert.ToInt32(words[1]);
                        if (width <= 0)
                            width = _bitmapWidth;
                        else
                            height = (int)((float)_bitmapHeight * (float)width / (float)_bitmapWidth);
                    }
                }

                if (words.Length == 4) {
                    if ((words[0].Equals("WIDTH"))) {
                        width = Convert.ToInt32(words[1]);
                        if (width <= 0)
                            width = _bitmapWidth;
                        else
                            height = (int)((float)_bitmapHeight * (float)width / (float)_bitmapWidth);
                    } else {
                        if ((words[2].Equals("WIDTH"))) {
                            width = Convert.ToInt32(words[3]);
                            if (width <= 0)
                                width = _bitmapWidth;
                            else
                                height = (int)((float)_bitmapHeight * (float)width / (float)_bitmapWidth);
                        }
                    }
                }
            } 

            // Fillscreen() is not needed while actively streaming
            // when there are no players, screen is out of date and refreshed on a new connect.
            // Since snapshot is asynchronous to that logic, we force a new fill
            fillScreen();

            // Convert the screen to a PNG image
            Bitmap bmp = new Bitmap(_bitmapWidth, _bitmapHeight, PixelFormat.Format32bppRgb);

            //Create a BitmapData and Lock all pixels to be written 
            BitmapData bmpData = bmp.LockBits(new System.Drawing.Rectangle(0, 0, bmp.Width, bmp.Height),
                ImageLockMode.WriteOnly, bmp.PixelFormat);

            //Copy the data from the byte array into BitmapData.Scan0
            System.Runtime.InteropServices.Marshal.Copy(screen, 0, bmpData.Scan0, screen.Length);

            //Unlock the pixels
            bmp.UnlockBits(bmpData);

            if (Program.maskValid == true) {
                using (Graphics g = Graphics.FromImage(bmp)) {
                    g.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
                    Brush blackBrush = new SolidBrush(Color.Black);

                    // Create array of rectangles.
                    RectangleF[] rects = {new RectangleF( 0.0F, 0.0F, _bitmapWidth, Program.maskY),
                                             new RectangleF(0.0F, Program.maskY, Program.maskX, Program.maskHeight),
                                             new RectangleF(Program.maskX + Program.maskWidth, Program.maskY, _bitmapWidth - Program.maskX - Program.maskWidth, Program.maskHeight),
                                             new RectangleF(0.0F, Program.maskY + Program.maskHeight, _bitmapWidth, _bitmapHeight - Program.maskY - Program.maskHeight)};

                    // Draw rectangles to screen.
                    g.FillRectangles(blackBrush, rects);
                }
            }

            if (width != _bitmapWidth) {
                Size newsz = new Size(width, height);
                Bitmap resized = new Bitmap(bmp, newsz);
                bmp.Dispose();
                bmp = resized;
            }

            byte[] buffer = null;
            try {
                using (MemoryStream stream = new MemoryStream()) {
                    bmp.Save(stream, ImageFormat.Png);
                    buffer = stream.ToArray();
                }
                context.Response.ContentType = "image/png";
                context.Response.ContentLength64 = buffer.Length;
                // context.Response.Headers.Add("Expires", "Sat, 1 Jan 2011 01:01:01 GMT");
            } catch {
                context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
            }
            context.Response.KeepAlive = false;

            System.IO.Stream output = context.Response.OutputStream;
            try {
                output.Write(buffer, 0, buffer.Length);
                output.Close();
            } catch (Exception) {
                // Oh well
            }

            // Ready to receive the next request
            listener.BeginGetContext(sendScreenShot, listener);
        }

        /// <summary>
        /// We explored many types of Zlib compression and ultimately settled on BITMAP. Read our paper for further details.
        /// </summary>
        private enum CompressionType {
            RGB16 = 0,
            ALPHA0 = 1,
            ALPHA255 = 2,
            VANILLA = 3,
            BITMAP = 4
        };
        
        /// <summary>
        /// 
        /// </summary>
        /// <param name="x"></param>
        /// <param name="y"></param>
        /// <param name="w"></param>
        /// <param name="h"></param>
        /// <returns></returns>
        public GCbuf GetRect(int x, int y, int w, int h) {
            return GetRect(x, y, w, h 
#if PRODUCTION
                , CompressionType.BITMAP
#endif
                );
        }

       /// <summary>
       /// Get the pixels within the update rectangle in CompressionType encoding
       /// </summary>
       /// <param name="x"></param>
       /// <param name="y"></param>
       /// <param name="w"></param>
       /// <param name="h"></param>
       /// <param name="type"></param>
       /// <returns></returns>
        private GCbuf GetRect(int x, int y, int w, int h, CompressionType type) {
            if (loaded == false)
                return null;

            GCbuf retRect = null;
            try {
                retRect = new GCbuf();
            } catch (Exception e) {
                // One reason for failure is when the client stops accepting new updates
                MessageBox.Show("FATAL: Memory allocation failed " + e.Message);
                Environment.Exit(1);
            }

            try {
                GetChangesBuffer getChangesBuffer = (GetChangesBuffer)Marshal.PtrToStructure(_getChangesBuffer, typeof(GetChangesBuffer));
                IntPtr start;
                byte[] membuf = new byte[w * sizeof(UInt32)];
                int bmCount = 0;
                int dtCount = w * h;

                if (type == CompressionType.RGB16)
                    retRect.Length = w * h * sizeof(UInt16);
                else if (type == CompressionType.BITMAP)
                    retRect.Length = w * h * sizeof(byte);      // Just the space for the bitmap
                else
                    retRect.Length = w * h * sizeof(UInt32);    // buffers are always width*height - we waste the remaining to ease in memory management and fragmentation

                unsafe {
                    start = new IntPtr(((Int32*)getChangesBuffer.UserBuffer) + ((y * _bitmapWidth) + x));
                }
                for (int j = 0; j < h; j++) {
                    try {
                        switch (type) {
                            case CompressionType.BITMAP:
                                Marshal.Copy(start, membuf, 0, w * sizeof(UInt32));

                                int indx = ((y + j) * _bitmapWidth + x) * sizeof(UInt32);
                                for (int i = 0; i < (w * sizeof(UInt32)); i += sizeof(UInt32), indx += sizeof(UInt32)) {
                                    if ((membuf[i] == screen[indx]) && (membuf[i + 1] == screen[indx + 1]) && (membuf[i + 2] == screen[indx + 2]))
                                        retRect.buf[bmCount++] = 0x00;
                                    else {
                                        retRect.buf[bmCount++] = 0xFF;
                                        retRect.buf[dtCount++] = screen[indx] = membuf[i];
                                        retRect.buf[dtCount++] = screen[indx + 1] = membuf[i + 1];
                                        retRect.buf[dtCount++] = screen[indx + 2] = membuf[i + 2];

                                        retRect.Length += 3;
                                    }
                                }
                                break;

#if !PRODUCTION
                            case CompressionType.RGB16:
                                Marshal.Copy(start, membuf, 0, w * sizeof(UInt32));
                                for (int i = 0; i < (w * sizeof(UInt32)); i += sizeof(UInt32)) {
                                    UInt16 rgb;

                                    rgb = (ushort)(membuf[i] & 0xF8); // Blue
                                    rgb += (ushort)((membuf[i + 1] & 0xFE) << 5); // Green
                                    rgb += (ushort)((membuf[i + 2] * 0xF8) << 11); // Red
                                    retRect.buf[j * w * sizeof(UInt16) + i] = (byte)((rgb & 0xFF00) >> 8);
                                    retRect.buf[j * w * sizeof(UInt16) + i + 1] = (byte)(rgb & 0x00FF);
                                }
                                break;

                            

                            case CompressionType.ALPHA0:
                            case CompressionType.ALPHA255:
                                Marshal.Copy(start, retRect.buf, j * w * sizeof(UInt32), w * sizeof(UInt32));

                                int indx = ((y + j) * _bitmapWidth + x) * sizeof(UInt32);
                                int ind = (j * w) * sizeof(UInt32);
                                for (int i = 0; i < (w * sizeof(UInt32)); i += sizeof(UInt32)) {
                                    if ((retRect.buf[ind] == screen[indx]) &&
                                        (retRect.buf[ind + 1] == screen[indx + 1]) &&
                                        (retRect.buf[ind + 2] == screen[indx + 2])) {
                                        if (type == CompressionType.ALPHA0) {
                                            retRect.buf[ind] = 0x00;
                                            retRect.buf[ind + 1] = 0x00;
                                            retRect.buf[ind + 2] = 0x00;
                                        }

                                        if (type == CompressionType.ALPHA255) {
                                            retRect.buf[ind] = 0xFF;
                                            retRect.buf[ind + 1] = 0xFF;
                                            retRect.buf[ind + 2] = 0xFF;
                                        }
                                        retRect.buf[ind + 3] = 0;
                                    } else {
                                        if (type == CompressionType.ALPHA255) {
                                            screen[indx] = retRect.buf[ind];
                                            screen[indx + 1] = retRect.buf[ind + 1];
                                            screen[indx + 2] = retRect.buf[ind + 2];
                                        }
                                        retRect.buf[ind + 3] = 0xFF;
                                    }
                                    indx += sizeof(UInt32);
                                    ind += sizeof(UInt32);
                                }
                                break;
#endif
                        }
                    } catch (Exception e) {
                        MessageBox.Show("FATAL: Failure to copy framebuffer data " + e.StackTrace);
                        Environment.Exit(1);
                    }

                    unsafe {
                        start = new IntPtr((Int32*)start + _bitmapWidth);
                    }
                }

                return retRect;
            } catch (Exception e) {
                // Debug.WriteLine("FATAL: Failed to capture pixmap. Please report to Surendar Chandra. Ignoring for now");
                MessageBox.Show("FATAL: Failed to capture pixmap. Please report to displaycast@fxpal.com " + e.Message);
                // myNotifyIcon.ShowBalloonTip(500, "Title", "Tip text", ToolTipIcon.Info);
            }
            return retRect;
        }
        #endregion

        #region mirror driver interaction
#if PRODUCTION
        private const int PollInterval = 16 /* 100 */; // Read our paper for why we chose this interval
#else
#if DEBUG_EXP_1
        private const int PollInterval = 1;
#endif
#endif
        private ManualResetEvent _terminatePollingThread;
        private ManualResetEvent _pollingThreadTerminated;
        private const int PollingThreadTerminationTimeout = 10000;

        private static string driverInstanceName = "";
		private static IntPtr _getChangesBuffer = IntPtr.Zero;
		private Thread _pollingThread = null;
		private const string driverDeviceNumber = "DEVICE0";
		private const string driverMiniportName = "dfmirage";
		private const string driverName = "Mirage Driver";
		private static RegistryKey _registryKey;

        /// <summary>
        /// 
        /// </summary>
        private static void LoadDriver() {
            var device = new DisplayDevice();
            var deviceMode = new DeviceMode {
                dmDriverExtra = 0
            };

            device.CallBack = Marshal.SizeOf(device);
            deviceMode.dmSize = (short)Marshal.SizeOf(deviceMode);
            deviceMode.dmBitsPerPel = Screen.PrimaryScreen.BitsPerPixel;

            if (deviceMode.dmBitsPerPel == 24)
                deviceMode.dmBitsPerPel = 32;

            if ((_bitmapBpp = deviceMode.dmBitsPerPel) != 32)
                throw new Exception("Unsupported bitmap format");

            deviceMode.dmDeviceName = string.Empty;
            deviceMode.dmFields = (DM_BITSPERPEL | DM_PELSWIDTH | DM_PELSHEIGHT | DM_POSITION);
            _bitmapHeight = deviceMode.dmPelsHeight = Screen.PrimaryScreen.Bounds.Height;
            _bitmapWidth = deviceMode.dmPelsWidth = Screen.PrimaryScreen.Bounds.Width;

            bool deviceFound;
            uint deviceIndex = 0;

            String drivers = "";
            while (deviceFound = NativeMethods.EnumDisplayDevices(null, deviceIndex, ref device, 0)) {
                // Trace.WriteLine("DEBUG: Found display driver: " + device.DeviceString + " " + device.DeviceName);
                if (device.DeviceString == driverName)
                    break;
                drivers = drivers + " " + device.DeviceString;
                deviceIndex++;
            }
            if (!deviceFound) {
                // MessageBox.Show("The appropriate mirror driver is not loaded. Only found " + drivers, "FATAL");
                throw new Exception("Mirror driver not loaded");
            }

            driverInstanceName = device.DeviceName;

            const string driverRegistryPath = "SYSTEM\\CurrentControlSet\\Hardware Profiles\\Current\\System\\CurrentControlSet\\Services";
            _registryKey = Registry.LocalMachine.OpenSubKey(driverRegistryPath, true);
            if (_registryKey != null) {
                _registryKey = _registryKey.CreateSubKey(driverMiniportName);

                if (_registryKey != null)
                    _registryKey = _registryKey.CreateSubKey(driverDeviceNumber);
                else
                    throw new Exception("Couldn't open registry key");
            } else
                throw new Exception("Couldn't open registry key");

            //			_registryKey.SetValue("Cap.DfbBackingMode", 0);
            //			_registryKey.SetValue("Order.BltCopyBits.Enabled", 1);

            _registryKey.SetValue("Cap.DfbBackingMode", 3);

            _registryKey.SetValue("Screen.ForcedBpp", 32);
            _registryKey.SetValue("Pointer.Enabled", 1);
            _registryKey.SetValue("Attach.ToDesktop", 1);

            try {
                SafeChangeDisplaySettingsEx(device.DeviceName, ref deviceMode, IntPtr.Zero, CDS_UPDATEREGISTRY, IntPtr.Zero);
                SafeChangeDisplaySettingsEx(device.DeviceName, ref deviceMode, IntPtr.Zero, 0, IntPtr.Zero);
            } catch {
            }

            try {
                if (!mapSharedBuffers())
                    throw new InvalidOperationException("mapSharedBuffers failed");
            } catch {
                throw new InvalidOperationException("mapSharedBuffers failed.");
            }
        }


        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        public bool Load() {
            LoadDriver();

            // Fill our internal access copy
            fillScreen();

			if (_terminatePollingThread == null)
				_terminatePollingThread = new ManualResetEvent(false);
			else
				_terminatePollingThread.Reset();

            _pollingThread = new Thread(pollingThreadProc) {
                IsBackground = true
            };
            _pollingThread.Start();

            loaded = true;
            return true;
		}

        /// <summary>
        /// 
        /// </summary>
        private static void UnloadDriver() {

            unmapSharedBuffers();

            var deviceMode = new DeviceMode();
            deviceMode.dmSize = (short)Marshal.SizeOf(typeof(DeviceMode));
            deviceMode.dmDriverExtra = 0;
            deviceMode.dmFields = (DM_BITSPERPEL | DM_PELSWIDTH | DM_PELSHEIGHT | DM_POSITION);

            var device = new DisplayDevice();
            device.CallBack = Marshal.SizeOf(device);
            deviceMode.dmDeviceName = string.Empty;
            uint deviceIndex = 0;
            while (NativeMethods.EnumDisplayDevices(null, deviceIndex, ref device, 0)) {
                if (device.DeviceString.Equals(driverName))
                    break;

                deviceIndex++;
            }

            Debug.Assert(_registryKey != null);
            _registryKey.SetValue("Attach.ToDesktop", 0);
            _registryKey.Close();

            deviceMode.dmDeviceName = driverMiniportName;

            if (deviceMode.dmBitsPerPel == 24)
                deviceMode.dmBitsPerPel = 32;

            try {
                SafeChangeDisplaySettingsEx(device.DeviceName, ref deviceMode, IntPtr.Zero, CDS_UPDATEREGISTRY, IntPtr.Zero);
                SafeChangeDisplaySettingsEx(device.DeviceName, ref deviceMode, IntPtr.Zero, 0, IntPtr.Zero);
            } catch {
            }
        }

        /// <summary>
        /// 
        /// </summary>
        public void Unload() {
            if (_pollingThreadTerminated == null)
                _pollingThreadTerminated = new ManualResetEvent(false);
            else
                _pollingThreadTerminated.Reset();

            // Terminate polling thread
            if (_terminatePollingThread != null)
                _terminatePollingThread.Set();

            // Wait for it...
            if (_pollingThread != null) {
                if (!_pollingThreadTerminated.WaitOne(PollingThreadTerminationTimeout, false)) {
                    try {
                        _pollingThread.Abort();
                    } catch (System.Threading.ThreadAbortException) {
                    } catch {
                    }
                }
            }

            UnloadDriver();
        }

#if !PRODUCTION
        public class compressor {
            GCbuf data;
            public int retvalue = -1;

            public compressor(GCbuf data) {
                this.data = data;
            }

            public void bitmapCompressData() {
                byte[] bm = new byte[data.Length/4];
                byte[] dt = new byte[data.Length];
                int bi = 0, di = 0;

                for (int i=0; i<data.Length; i+=sizeof(UInt32))
                    if (data.buf[i+3] == 0) 
                        bm[bi++]=0x00;
                    else {
                        bm[bi++]=0xFF;
                        dt[di++] = data.buf[i];
                        dt[di++] = data.buf[i+1];
                        dt[di++] = data.buf[i+2];
                    }
                MemoryStream outbuf = new MemoryStream();
                DeflateStream compress = new DeflateStream(outbuf, CompressionMode.Compress);

                compress.Write(bm, 0, bi);
                compress.Write(dt, 0, di);
                compress.Close();
                byte[] compData = outbuf.ToArray();
                retvalue = compData.Length;

                compress.Dispose();
                outbuf.Dispose();
            }

            public void compressData() {
                MemoryStream outbuf = new MemoryStream();
                DeflateStream compress = new DeflateStream(outbuf, CompressionMode.Compress);
                
                compress.Write(data.buf, 0, data.Length);
                compress.Close();
                byte[] compData = outbuf.ToArray();
                retvalue = compData.Length;

                compress.Dispose();
                outbuf.Dispose();
            }
        }
#endif

        static DateTime startTime = DateTime.Now;
        static bool restartingDriver = false;

#if !PRODUCTION
#if DEBUG_EXP_1
        static TimeSpan diffT;
        static StreamWriter log = null;
#endif
#endif 
        static int idleCount = 0;   // Powerpoint slides can lead to static images. Send a I-frame after 30*PollingInterval of inactivity
        static long oldCounter = long.MaxValue;

        /// <summary>
        /// 
        /// </summary>
		private void pollingThreadProc() {
            // Why bother to poll while there is no one to receive the updates
            while (DesktopChange == null)
                Thread.Sleep(1);

			while (true) {
                if (restartingDriver == false) {
                    var getChangesBuffer = (GetChangesBuffer)Marshal.PtrToStructure(_getChangesBuffer, typeof(GetChangesBuffer));   // Moved it inside the loop in hope of fixing the hibernation bug
                    var buffer = (ChangesBuffer)Marshal.PtrToStructure(getChangesBuffer.Buffer, typeof(ChangesBuffer));

                    // Initialize oldCounter
                    if (oldCounter == long.MaxValue)
                        oldCounter = buffer.counter;

                    if (oldCounter != buffer.counter) {
                        // Trace.WriteLine("Updates: " + (buffer.counter - oldCounter));
                        for (long currentChange = oldCounter; currentChange != buffer.counter; currentChange++) {
                            if (currentChange >= ChangesBuffer.MAXCHANGES_BUF)
                                currentChange = 0;
#if PRODUCTION
                            DesktopChange(this,
                                              new DesktopChangeEventArgs(buffer.pointrect[currentChange].rect.x1,
                                                                         buffer.pointrect[currentChange].rect.y1,
                                                                         buffer.pointrect[currentChange].rect.x2,
                                                                         buffer.pointrect[currentChange].rect.y2,
                                                                         (OperationType)buffer.pointrect[currentChange].type));
#else
                        int x, y, w, h;
                        x = buffer.pointrect[currentChange].rect.x1;
                        y = buffer.pointrect[currentChange].rect.y1;
                        w = (buffer.pointrect[currentChange].rect.x2 - buffer.pointrect[currentChange].rect.x1);
                        h = (buffer.pointrect[currentChange].rect.y2 - buffer.pointrect[currentChange].rect.y1);


#if DEBUG_EXP_1
                        if (log == null) {
                            log = new StreamWriter("c:/mmsys_1.txt");
                            log.AutoFlush = true;
                        }
                        diffT = (DateTime.Now - startTime);
                        log.WriteLine(diffT.TotalMilliseconds + " " + x + " " + y + " " + w + " " + h);
                        continue;
#endif

#if DEBUG_EXP_2
                        if (log == null) {
                            log = new StreamWriter("c:/3.txt");
                            log.AutoFlush = true;
                        }
                       
                        int a0Len, a255Len, rgbLen, vanillaLen, origLen, bitmapLen;

                        origLen = w * h * sizeof(UInt32);
                        diffT = (DateTime.Now - startTime);

                        GCbuf a0 = GetRect(buffer.pointrect[currentChange].rect.x1,
                         buffer.pointrect[currentChange].rect.y1,
                         (buffer.pointrect[currentChange].rect.x2 - buffer.pointrect[currentChange].rect.x1),
                         (buffer.pointrect[currentChange].rect.y2 - buffer.pointrect[currentChange].rect.y1),
                         CompressionType.ALPHA0);
                        int same = 0, diff = 0;
                        for (int i = 0; i < a0.Length; i+=sizeof(UInt32))
                            if (a0.buf[i+3] == 0)
                                same++;
                            else
                                diff++;
                        compressor a0C = new compressor(a0);
                        Thread a0t = new Thread(new ThreadStart(a0C.compressData));
                        a0t.Start();

                        GCbuf bitmap = new GCbuf();
                        Buffer.BlockCopy(a0.buf, 0, bitmap.buf, 0, a0.Length);
                        bitmap.Length = a0.Length;
                        compressor bC = new compressor(bitmap);
                        Thread bT = new Thread(new ThreadStart(bC.bitmapCompressData));
                        bT.Start();

                        GCbuf vanilla = GetRect(buffer.pointrect[currentChange].rect.x1,
                            buffer.pointrect[currentChange].rect.y1,
                            (buffer.pointrect[currentChange].rect.x2 - buffer.pointrect[currentChange].rect.x1),
                            (buffer.pointrect[currentChange].rect.y2 - buffer.pointrect[currentChange].rect.y1),
                            CompressionType.VANILLA);
                        compressor vC = new compressor(vanilla);
                        Thread vt = new Thread(new ThreadStart(vC.compressData));
                        vt.Start();

                        GCbuf rgb = GetRect(buffer.pointrect[currentChange].rect.x1,
                            buffer.pointrect[currentChange].rect.y1,
                            (buffer.pointrect[currentChange].rect.x2 - buffer.pointrect[currentChange].rect.x1),
                            (buffer.pointrect[currentChange].rect.y2 - buffer.pointrect[currentChange].rect.y1),
                            CompressionType.RGB16);
                        compressor rC = new compressor(rgb);
                        Thread rt = new Thread(new ThreadStart(rC.compressData));
                        rt.Start();

                        GCbuf a255 = GetRect(buffer.pointrect[currentChange].rect.x1,
                            buffer.pointrect[currentChange].rect.y1,
                            (buffer.pointrect[currentChange].rect.x2 - buffer.pointrect[currentChange].rect.x1),
                            (buffer.pointrect[currentChange].rect.y2 - buffer.pointrect[currentChange].rect.y1),
                            CompressionType.ALPHA255);
                        compressor a2C = new compressor(a255);
                        Thread a2t = new Thread(new ThreadStart(a2C.compressData));
                        a2t.Start();

                        a0t.Join();
                        a0Len = a0C.retvalue;

                        vt.Join();
                        vanillaLen = vC.retvalue;

                        rt.Join();
                        rgbLen = rC.retvalue;

                        a2t.Join();
                        a255Len = a2C.retvalue;

                        bT.Join();
                        bitmapLen = bC.retvalue;

                        log.WriteLine(diffT.TotalMilliseconds + " S " + same + " D " + diff + " O " + (w * h * sizeof(UInt32)) + " R " + rgbLen + " A0 " + a0Len + " A255 " + a255Len + " V " + vanillaLen + " B " + bitmapLen);
                        
                        a0.Dispose();
                        a255.Dispose();
                        rgb.Dispose();
                        vanilla.Dispose();
                        bitmap.Dispose();

                        continue;                 
#endif

#if DEBUG_EXP_3
                        if (log == null) {
                            log = new StreamWriter("c:/mmsys_3.txt");
                            log.AutoFlush = true;
                        }

                        diffT = (DateTime.Now - startTime);
                        GCbuf bitmap = GetRect(buffer.pointrect[currentChange].rect.x1,
                         buffer.pointrect[currentChange].rect.y1,
                         (buffer.pointrect[currentChange].rect.x2 - buffer.pointrect[currentChange].rect.x1),
                         (buffer.pointrect[currentChange].rect.y2 - buffer.pointrect[currentChange].rect.y1),
                         CompressionType.ALPHA0);

                        compressor bC = new compressor(bitmap);
                        bC.bitmapCompressData();
                        log.WriteLine(diffT.TotalMilliseconds + " " + bC.retvalue);
                        bitmap.Dispose();
#endif
#endif
                        }
                        oldCounter = buffer.counter;
                        idleCount = 0;
                    } else {
#if PRODUCTION
                        if (idleCount++ == (1000 / PollInterval)) {   // One second
                            DesktopChange(this, new DesktopChangeEventArgs(0, 0, _bitmapWidth, _bitmapHeight, OperationType.dmf_dfo_IGNORE));
                            // Trace.WriteLine("DEBUG: Sending iFrame oldCount: " + oldCounter + " buffer " + buffer.counter);
                            idleCount = 0;
                        }
#endif
                    }
                }

				// Just to prevent 100-percent CPU load and to provide thread-safety use manual reset event instead of simple in-memory flag.
                if (_terminatePollingThread.WaitOne(PollInterval, false)) {
					Trace.WriteLine("The thread now exits");
					break;
				}
                
            }

			// We can be sure that _pollingThreadTerminated exists
			_pollingThreadTerminated.Set();
		}

		private static IntPtr _globalDC;
        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
		private static bool mapSharedBuffers() {
            if ((_globalDC = NativeMethods.CreateDC(driverInstanceName, null, null, IntPtr.Zero)) == IntPtr.Zero)
                throw new Win32Exception(Marshal.GetLastWin32Error());

			if (_getChangesBuffer != IntPtr.Zero)
				Marshal.FreeHGlobal(_getChangesBuffer);

			_getChangesBuffer = Marshal.AllocHGlobal(Marshal.SizeOf(typeof (GetChangesBuffer)));

            return (NativeMethods.ExtEscape(_globalDC, Map, 0, IntPtr.Zero, Marshal.SizeOf(typeof(GetChangesBuffer)), _getChangesBuffer) > 0);
		}

        /// <summary>
        /// 
        /// </summary>
		private static void unmapSharedBuffers() {
            if (NativeMethods.ExtEscape(_globalDC, UnMap, Marshal.SizeOf(typeof(GetChangesBuffer)), _getChangesBuffer, 0, IntPtr.Zero) < 0)
				throw new Win32Exception(Marshal.GetLastWin32Error());

			Marshal.FreeHGlobal(_getChangesBuffer);
			_getChangesBuffer = IntPtr.Zero;

            NativeMethods.ReleaseDC(IntPtr.Zero, _globalDC);
		}
        #endregion

        #region IDisposable Members
        private bool disposed = false;
        /// <summary>
        /// 
        /// </summary>
        ~DesktopMirror () {
            Dispose(false);
        }

        /// <summary>
        /// 
        /// </summary>
        public void Dispose() {
            Dispose(true);
            GC.SuppressFinalize(this);
        } 
        
        /// <summary>
        /// 
        /// </summary>
        /// <param name="disposing"></param>
        protected virtual void Dispose(bool disposing) {
            if (!this.disposed) {
                if (disposing) {
                    Unload();
                }

                // Note disposing has been done.
                disposed = true;
            }
        }
        #endregion

        #region Hiberation restart
        public static void SystemEvents_PowerModeChanged(object sender, PowerModeChangedEventArgs e) {
            // throw new NotImplementedException();
            restartingDriver = true;
            Trace.WriteLine("DEBUG: Hibernation detected " + e);

            UnloadDriver();
            LoadDriver();

            oldCounter = long.MaxValue;
            restartingDriver = false;
        }
        #endregion

    }
}
