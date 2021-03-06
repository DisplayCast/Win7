﻿// Copyright (c) 2012, Fuji Xerox Co., Ltd.
// All rights reserved.
// Author: Surendar Chandra, FX Palo Alto Laboratory, Inc.

// #define SHOW_STATS 
// #define USE_BITMAP_COMPRESS

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

#if USE_IONIC_ZLIB
using Ionic.Zlib;
using Ionic.Crc;
#else
using System.IO.Compression;
#endif

using Shared;
using ZeroconfService;

using Microsoft.Win32;

using Mirror.Driver;

namespace FXPAL.DisplayCast.Streamer {
    /// <summary>
    /// 
    /// </summary>
    class sendUpdate : IDisposable {
        public NetworkStream newStream; // Who should get this update. null means everyone
        public GCbuf buf;
        public int x;                   // screen region to send data for
        public int y;
        public int w;
        public int h;
        // public int width;
        // public int height;
        // public int maskX;
        // public int maskY;
        // public int maskWidth;
        // public int maskHeight;

        public EventWaitHandle proceedToSend = null;
        static ArrayList events = new ArrayList(Environment.ProcessorCount);

        #region IDisposable Members
        private bool disposed = false;
        /// <summary>
        /// 
        /// </summary>
        ~sendUpdate() {
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
                    if (buf != null)
                        buf.Dispose();
                    outbuf.Dispose();
                    // proceedToSend.Dispose();
                    if (proceedToSend != null)
                        lock (events.SyncRoot) {    // Reuse
                            events.Add(proceedToSend);
                        }
                }

                // Note disposing has been done.
                disposed = true;
            }
        }
        #endregion

        /// <summary>
        /// Send updates. if newStream is not null, send to just this streams, otherwise everyone gets this update
        /// </summary>
        /// <param name="newStream"></param>
        /// <param name="buf"></param>
        /// <param name="x"></param>
        /// <param name="y"></param>
        /// <param name="w"></param>
        /// <param name="h"></param>
        public sendUpdate(NetworkStream newStream, GCbuf buf, int x, int y, int w, int h) { // , int width, int height, int maskX, int maskY, int maskWidth, int maskHeight) {
            this.newStream = newStream;
            this.buf = buf;
            this.x = x;
            this.y = y;
            this.w = w;
            this.h = h;
            // this.width = width;
            // this.height = height;
            // this.maskX = maskX;
            // this.maskY = maskY;
            // this.maskWidth = maskWidth;
            // this.maskHeight = maskHeight;
        }

        /// <summary>
        /// 
        /// </summary>
        public void allocEventHandle() {
            // Wait to allocate it till we actually need to send something. Typically, lots of updates are created and destroyed before ever sending anything
            lock (events.SyncRoot) {
                if (events.Count == 0)
                    proceedToSend = new EventWaitHandle(false, EventResetMode.AutoReset);
                else {
                    proceedToSend = (EventWaitHandle)events[0];
                    proceedToSend.Reset();
                    events.RemoveAt(0);
                }
            }
        }

        private MemoryStream outbuf;
        /// <summary>
        /// 
        /// </summary>
        /// <param name="arg"></param>
        public void compressSend(Object arg) {
            // Now send this update off
            UInt32 hdr;
            byte[] hdrbuf;
            streamThread parent = (streamThread)arg;

            Debug.Assert(buf != null);

            try {
                if (buf.Length == -1)
                    buf = parent._mirror.GetRect(x, y, w, h);
            } catch {
                Trace.WriteLine("getRect failed");
                return;
            }
            if (buf == null)
                return;

            outbuf = new MemoryStream();

            int checkSum = 1;
            using (DeflateStream compress = new DeflateStream(outbuf, CompressionMode.Compress
#if USE_IONIC_ZLIB
                , CompressionLevel.BestSpeed /* CompressionLevel.BestCompression */)){
                compress.FlushMode = FlushType.Sync;
#else
                )) { 
#endif           

                hdr = ((UInt32)DesktopMirror._bitmapWidth << 16) + ((UInt32)DesktopMirror._bitmapHeight & 0xFFFF);
                hdrbuf = BitConverter.GetBytes(IPAddress.HostToNetworkOrder((Int32)hdr));
                compress.Write(hdrbuf, 0, hdrbuf.Length);
                checkSum = Adler32.ComputeChecksum(checkSum, hdrbuf, 0, hdrbuf.Length);

                hdr = ((UInt32)Program.maskX << 16) + ((UInt32)Program.maskY & 0xFFFF);
                hdrbuf = BitConverter.GetBytes(IPAddress.HostToNetworkOrder((Int32)hdr));
                compress.Write(hdrbuf, 0, hdrbuf.Length);
                checkSum = Adler32.ComputeChecksum(checkSum, hdrbuf, 0, hdrbuf.Length);

                hdr = ((UInt32)Program.maskWidth << 16) + ((UInt32)Program.maskHeight & 0xFFFF);
                hdrbuf = BitConverter.GetBytes(IPAddress.HostToNetworkOrder((Int32)hdr));
                compress.Write(hdrbuf, 0, hdrbuf.Length);
                checkSum = Adler32.ComputeChecksum(checkSum, hdrbuf, 0, hdrbuf.Length);

                hdr = ((UInt32)x << 16) + ((UInt32)y & 0xFFFF);
                hdrbuf = BitConverter.GetBytes(IPAddress.HostToNetworkOrder((Int32)hdr));
                compress.Write(hdrbuf, 0, hdrbuf.Length);
                checkSum = Adler32.ComputeChecksum(checkSum, hdrbuf, 0, hdrbuf.Length);

                hdr = ((UInt32)w << 16) + ((UInt32)h & 0xFFFF);
                hdrbuf = BitConverter.GetBytes(IPAddress.HostToNetworkOrder((Int32)hdr));
                compress.Write(hdrbuf, 0, hdrbuf.Length);
                checkSum = Adler32.ComputeChecksum(checkSum, hdrbuf, 0, hdrbuf.Length);

                /*
    #if USE_BITMAP_COMPRESS
                byte[] bm = new byte[buf.Length / 4];
                byte[] dt = new byte[buf.Length];
                int bi = 0, di = 0;

                for (int i = 0; i < buf.Length; i += sizeof(UInt32))
                    if (buf.buf[i + 3] == 0)
                        bm[bi++] = 0x00;
                    else {
                        bm[bi++] = 0xFF;
                        dt[di++] = buf.buf[i];
                        dt[di++] = buf.buf[i + 1];
                        dt[di++] = buf.buf[i + 2];
                    }

                compress.Write(bm, 0, bi);
                compress.Write(dt, 0, di);
    #else
                compress.Write(buf.buf, 0, buf.Length);
    #endif
                 */
                compress.Write(buf.buf, 0, buf.Length);
            }

            byte[] compHdr = new byte[] { 0x58, 0x85 };
            
            byte[] compData = outbuf.ToArray();
            
            checkSum = Adler32.ComputeChecksum(checkSum, buf.buf, 0, buf.Length);
            int ncheckSum = IPAddress.HostToNetworkOrder(checkSum);
            // byte[] compCheckSum = BitConverter.GetBytes(IPAddress.HostToNetworkOrder(ncheckSum));
            byte[] compCheckSum = BitConverter.GetBytes(ncheckSum);

            hdr = (UInt32)(compHdr.Length + compData.Length + compCheckSum.Length);
            hdrbuf = BitConverter.GetBytes(hdr);
            // Trace.WriteLine("Size: " + (compData.Length));

            // buf.Dispose();
            // Trying to reduce the memory footprint
            // GC.Collect(0, GCCollectionMode.Optimized);
            // GC.Collect(0, GCCollectionMode.Forced);
#if SHOW_STATS
                    if (DateTime.Now.Second != parent.prevSec) {
                        parent.prevSec = DateTime.Now.Second;
                        Trace.WriteLine("DEBUG: Mbps - " + (parent.dataBytes * 8) / 1000000);
                        parent.dataBytes = 0;
                    }
                    parent.dataBytes += (hdrbuf.Length + compHdr.Length + compData.Length + compCheckSum.Length);
#endif
            try {
                this.proceedToSend.WaitOne();   // Wait till I am told to proceed to send
                // this.proceedToSend.Dispose();   // I no longer need this trigger. Dispose now rather than wait for Dispose()
                lock (events.SyncRoot) {    // Reuse
                    events.Add(proceedToSend);
                    proceedToSend = null;
                }
            } catch (Exception e) {
                MessageBox.Show(e.StackTrace);
                Environment.Exit(1);
            }

            if (this.newStream == null) {
#if !SHOW_STATS
                // Deleting inplace causes invalid invocation exception because it is inside the enumerator
                ArrayList failures = new ArrayList();
                foreach (System.Net.Sockets.NetworkStream clnt in parent.clients) {
                    try {
                        clnt.Write(hdrbuf, 0, hdrbuf.Length);
                        clnt.Write(compHdr, 0, compHdr.Length);
                        clnt.Write(compData, 0, compData.Length);
                        clnt.Write(compCheckSum, 0, compCheckSum.Length);
                    } catch (IOException ioe) {
                        Trace.WriteLine("TIMEOUT : " + ioe.Message);
                        // Could've been a timeout of could've been an actual error
                        clnt.Close();
                        failures.Add(clnt);
                    }
                }
                if (failures.Count > 0) {
                    foreach (System.Net.Sockets.NetworkStream clnt in failures) {
                        parent.clients.Remove(clnt);
                    }
                }
#endif
            } else {
                if (parent.clients.Count == 0) // Its been a while
                    parent._mirror.fillScreen();
                parent.clients.Add(this.newStream);

                try {
                    this.newStream.Write(hdrbuf, 0, hdrbuf.Length);
                    this.newStream.Write(compHdr, 0, compHdr.Length);
                    this.newStream.Write(compData, 0, compData.Length);
                    this.newStream.Write(compCheckSum, 0, compCheckSum.Length);
                } catch (IOException) {
                    this.newStream.Close();
                    parent.clients.Remove(this.newStream);
                }
            }
            buf.Dispose();
            buf = null;
            try {
                parent.proceedToNext.Set();
            } catch (Exception e) {
                MessageBox.Show(e.StackTrace);
                Environment.Exit(1);
            }
        }
    }
}
