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

using ZeroconfService;
using Shared;

using Microsoft.Win32;
using Mirror.Driver;

namespace FXPAL.DisplayCast.Streamer {
    /// <summary>
    /// Accepts new connections
    /// </summary>
    class serverThread {
        TcpListener svr;
        streamThread streamer;
        DesktopMirror _mirror;

        /// <summary>
        /// 
        /// </summary>
        /// <param name="svr"></param>
        /// <param name="_mirror"></param>
        /// <param name="streamer"></param>
        public serverThread(TcpListener svr, DesktopMirror _mirror, streamThread streamer) {
            this.svr = svr;
            this._mirror = _mirror;
            this.streamer = streamer;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="newStream"></param>
        private void sendIframe(NetworkStream newStream) {
            try {
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
#endif
                // screenbuf.buf = _mirror.screen;
                screenbuf.Length = _mirror.screen.Length;

                sendUpdate upd;
                if (Program.maskValid == true) 
                    upd = new sendUpdate(newStream, screenbuf, Program.maskX, Program.maskY, Program.maskWidth, Program.maskHeight);
                else
                    upd = new sendUpdate(newStream, screenbuf, 0, 0, DesktopMirror._bitmapWidth, DesktopMirror._bitmapHeight);
                lock (streamer.updates.SyncRoot) {
                    streamer.updates.Enqueue(upd);
                }
            } catch (IOException) {
                Trace.WriteLine("DEBUG: Event creator causes an internal Win32 exception. Just ignore");
            }
        }

        /// <summary>
        /// First send the initial full frame
        /// </summary>
        public void process() {
            while (true) {
                TcpClient connAddr = svr.AcceptTcpClient();
                NetworkStream connStream = connAddr.GetStream();

                // connStream.WriteTimeout = 1000;
                sendIframe(connStream);
            }
        }
    }
}
