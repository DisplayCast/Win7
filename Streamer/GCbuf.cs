// Copyright (c) 2012, Fuji Xerox Co., Ltd.
// All rights reserved.
// Author: Surendar Chandra, FX Palo Alto Laboratory, Inc.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Collections;
using Mirror.Driver;
using System.Diagnostics;
using System.Windows.Forms;

namespace FXPAL.DisplayCast.Streamer {
    // Large objects are not garbage collected in release code. Hence, allocate screen[] size buffers all the time!!
    public class GCbuf : IDisposable {
        public byte[] buf;
        public int Length;
        private static ArrayList memPool = null;
        private static int allocatedPools = 0;

        /// <summary>
        /// 
        /// </summary>
        public GCbuf() {
            if (memPool == null)
                memPool = new ArrayList();

            lock (memPool.SyncRoot) {
                if (memPool.Count == 0) {
                    try {
                        buf = new byte[DesktopMirror._bitmapWidth * DesktopMirror._bitmapHeight * sizeof(UInt32)];
                    } catch (System.OutOfMemoryException om) {
                        Trace.WriteLine("Out of memory: allocated " + allocatedPools + "Exception: " + om);
                        throw new System.OutOfMemoryException("Too much pending buffers. Client likely not responding");
                    }
                    allocatedPools++;
                }  else {
                    foreach (byte[] data in memPool) {
                        buf = data;
                        memPool.Remove(buf);
                        break;
                    }
                }
                Debug.Assert(buf != null);
            }
        }

        #region IDisposable Members
        private bool disposed = false;
        /// <summary>
        /// 
        /// </summary>
        ~GCbuf() {
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
                // if (disposing) {
                Debug.Assert(buf != null);

                lock (memPool.SyncRoot) {
                    memPool.Add(buf);
                }

                // Note disposing has been done.
                disposed = true;
            }
        }
        #endregion
    }
}
