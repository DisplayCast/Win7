﻿// Copyright (c) 2012, Fuji Xerox Co., Ltd.
// All rights reserved.
// Author: Surendar Chandra, FX Palo Alto Laboratory, Inc.

// #define SHOW_STATS

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
using Microsoft.Win32;
using Mirror.Driver;

namespace FXPAL.DisplayCast.Streamer {
    /// <summary>
    /// 
    /// </summary>
    class streamThread : IDisposable {
        public readonly DesktopMirror _mirror;
        public Queue     updates;       // Queue to store all rectangles that needed to be sent
        public ArrayList clients;   // List of clients to send the buffers to
        public Queue     preFlightUpdates;  // Queue of all updates that are being compressed in a separate thread.
        public EventWaitHandle  proceedToNext;  // Used by compression thread to signal that is done

        #region IDisposable Members
        private bool disposed = false;
        /// <summary>
        /// 
        /// </summary>
        ~streamThread() {
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
                    _mirror.Unload();
                    updates.Clear();
                    clients.Clear();
                    preFlightUpdates.Clear();
                    proceedToNext.Dispose();
                }

                // Note disposing has been done.
                disposed = true;
            }
        }
        #endregion

        /// <summary>
        /// 
        /// </summary>
        /// <param name="_mirror"></param>
        public streamThread(DesktopMirror _mirror) {
            this._mirror = _mirror;
            
            updates = new Queue();
            clients = new ArrayList();
        }

        /// <summary>
        /// 
        /// </summary>
        private void coordinateFlight() {
            sendUpdate upd;

            while (true) {
                if (preFlightUpdates.Count == 0) {
                    Thread.Sleep(5);
                    continue;
                }
 
                lock (preFlightUpdates.SyncRoot) {
                    upd = (sendUpdate)preFlightUpdates.Dequeue();
                }
                if (upd == null)
                    continue;

                Debug.Assert(upd.proceedToSend != null);
                upd.proceedToSend.Set();    // The first one should go. If later threads were finished, they just wait
                proceedToNext.WaitOne();    // Wait forever till the worked thread is done
                upd.Dispose();
            }
        }

#if SHOW_STATS
        public int prevSec = DateTime.Now.Second;
        public long dataBytes = 0;
#endif 
        /// <summary>
        /// 
        /// </summary>
        public void process() {
            sendUpdate upd;
            int numThreads = Environment.ProcessorCount;

            proceedToNext = new EventWaitHandle(false, EventResetMode.AutoReset);
            preFlightUpdates = new Queue();

            Thread coordinate = new Thread(new ThreadStart(this.coordinateFlight));
            coordinate.Name = "SendCoordinator";
            coordinate.Start();

            // For some reason, doesn't work when installed (as opposed to run via debugger)
            // ThreadPool.SetMaxThreads(numThreads, numThreads);
            while (true) {
                // Yes, this is unsafe but saves from synchronization overhead. We do check again later on
                if (updates.Count == 0) {
                    Thread.Sleep(5);
                    continue;
                }

                if (preFlightUpdates.Count > numThreads) {
                    Thread.Sleep(5);
                    continue;
                }
                
                lock (updates.SyncRoot) {
                    if (updates.Count == 0)
                        continue;
                    // Trace.WriteLine("DEBUG: Pending updates " + updates.Count);
                    upd = (sendUpdate)updates.Dequeue();
                }

                // Maintain update propagation order by collecting bits before spawning off compress and send thread
                if (upd.buf == null)
                    upd.buf = _mirror.GetRect(upd.x, upd.y, upd.w, upd.h);
                upd.allocEventHandle();

                /* 
                Thread t = new Thread(upd.compressSend);
                t.Name = "worker";
                t.Start(this);
                */
                // int workerThreads, portThreads;
                // ThreadPool.GetMaxThreads(out workerThreads, out portThreads);
                // Trace.WriteLine("DEBUG: Maxthreads = " +  workerThreads + " and " + portThreads);

                ThreadPool.QueueUserWorkItem(upd.compressSend, this);
                preFlightUpdates.Enqueue(upd);
            }   
        }
    }
}
