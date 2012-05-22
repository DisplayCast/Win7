// Copyright (c) 2012, Fuji Xerox Co., Ltd.
// All rights reserved.
// Author: Surendar Chandra, FX Palo Alto Laboratory, Inc.

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Linq;
using System.ServiceProcess;
using System.Text;
using System.Threading;
using System.Collections;

namespace FXPAL.DisplayCast.ControllerService {
    public partial class Service : ServiceBase {
        private ArrayList sessionList = new ArrayList();
        private ArrayList playerList = new ArrayList();
        private ArrayList streamerList = new ArrayList();
        private ArrayList archiverList = new ArrayList();
        private ArrayList sinkServicesList = new ArrayList();
        private ArrayList sourceServicesList = new ArrayList();

        private monitorPlayers players = null;
        private APIresponder api = null;
       /// <summary>
       /// 
       /// </summary>
       /// <param name="args"></param>
        protected override void OnStart(string[] args) {
            Start();
        }

        /// <summary>
        /// 
        /// </summary>
        public void Start() {
            players = new monitorPlayers(sessionList, playerList, streamerList, archiverList, sinkServicesList, sourceServicesList);
            api = new APIresponder(sessionList, playerList, streamerList, archiverList, sinkServicesList, sourceServicesList);

#if CONTROLLER_DEBUG_SERVICE
            while (true) {
                Thread.Sleep(100);
            }
#endif
        }

        /// <summary>
        /// 
        /// </summary>
        protected override void OnStop() {
            if (players != null) {
                try {
                    players.playerBrowser.Stop();
                    players.archiveBrowser.Stop();
                    players.streamerBrowser.Stop();
                } catch (Exception) {
                }
            }

            if (api != null) {
                try {
                    api.listener.Stop();
                } catch (Exception) {
                }
            }

            this.ExitCode = 0;
        }

        /// <summary>
        /// 
        /// </summary>
        public Service() {
            InitializeComponent();
        }
    }
}
