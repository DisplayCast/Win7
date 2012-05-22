// Copyright (c) 2012, Fuji Xerox Co., Ltd.
// All rights reserved.
// Author: Surendar Chandra, FX Palo Alto Laboratory, Inc.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Drawing;

using ZeroconfService;
using Shared;

using System.ComponentModel;
using System.Threading;
using System.Diagnostics;
using System.Collections;

namespace FXPAL.DisplayCast.ControllerService {
    // Monitors Bonjour for the status of Streamers/Archviers/Players
    class monitorPlayers {
        public NetServiceBrowser playerBrowser, streamerBrowser, archiveBrowser;

        private ArrayList sessions, players, streamers, archivers, sinkServices, sourceServices;

        #region Utility functions
        // Returns the appropriate Arraylist for the service
        private ArrayList getList(String type) {
            if (type.StartsWith(Shared.DisplayCastGlobals.PLAYER))
                return players;
            else if (type.StartsWith(Shared.DisplayCastGlobals.STREAMER))
                return streamers;
            else if (type.StartsWith(Shared.DisplayCastGlobals.ARCHIVER))
                return archivers;
            return null;
        }

        /// <summary>
        /// TXT records contain additional attributes of services
        /// </summary>
        /// <param name="service"></param>
        /// <param name="player"></param>
        private void processTXTrecord(NetService service, JSONSrcSink player) {
            byte[] txt = service.TXTRecordData;
            IDictionary dict = NetService.DictionaryFromTXTRecordData(txt);

            if (dict == null)
                return;

            // Remove all sessions from this Player so that we can add all the new entries from this TXT record
            ArrayList itemsToRemove = new ArrayList();
            lock (sessions.SyncRoot) {
                foreach (JSONSession nxtSess in sessions) {
                    if (service.Name.Equals(nxtSess.sinkId))
                        itemsToRemove.Add(nxtSess);
                };

                if (itemsToRemove.Count > 0) {
                    foreach (JSONSession sess in itemsToRemove)
                        sessions.Remove(sess);
                    itemsToRemove.Clear();
                }
            }

            foreach (DictionaryEntry kvp in dict) {
                String key = ((String)kvp.Key).ToUpper();
                String value = null;
                try {
                    value = Encoding.UTF8.GetString((byte[])kvp.Value);
                } catch {
                    // All displaycast values are strings!!
                    continue;
                }

                switch (key) {
                    case "NAME":
                        player.description = value;
                        break;

                    case "OSVERSION":
                        player.os = value;
                        break;

                    case "MACHINENAME":
                        player.machineName = value;
                        break;

                    case "LOCATIONID":
                        player.locationID = value;
                        break;

                    case "VERSION":
                        Trace.WriteLine("DEBUG: No use for version info in the API");
                        // player.version = Convert.ToDouble(value);
                        break;

                    case "IMAGEPORT":
                        Trace.WriteLine("DEBUG: No use for image port in the session object");
                        // player.imagePort = Convert.ToInt32(value);
                        break;

                    case "USERID":
                        player.userName = value;
                        break;

                    case "NEARBY":
                        player.nearBy = value;
                        break;

                    case "MASKPORT":
                        Trace.WriteLine("DEBUG: No use for Mask port info in the API");
                        break;

                    case "BLUETOOTH":
                        Trace.WriteLine("DEBUG: No use for Bluetooth ID's in the API");
                        break;

                    case "MASKSCREEN":
                        try {
                            char[] separator = { ' ', 'x' };
                            String[] words = value.Split(separator);

                            player.maskX = Convert.ToInt32(words[0]);
                            player.maskY = Convert.ToInt32(words[1]);
                            player.maskWidth = Convert.ToInt32(words[2]);
                            player.maskHeight = Convert.ToInt32(words[3]);
                        } catch (FormatException) {
                            player.maskX = player.maskY = player.maskWidth = player.maskHeight = 0;
                        }
                        break;

                    default:
                        if (key.StartsWith("SCREEN")) {    // Could be screen0, screen1 etc.
                            Rectangle oldRect = new Rectangle(player.x, player.y, player.width, player.height);
                            char[] separator = { ' ', 'x' };
                            String[] words = value.Split(separator);

                            Rectangle newRect = new Rectangle();
                            try {
                                newRect.X = Convert.ToInt32(words[0]);
                                newRect.Y = Convert.ToInt32(words[1]);
                                newRect.Width = Convert.ToInt32(words[2]);
                                newRect.Height = Convert.ToInt32(words[3]);
                            } catch (FormatException) {
                                continue;
                            }

                            oldRect = Rectangle.Union(oldRect, newRect);
                            player.x = oldRect.X;
                            player.y = oldRect.Y;
                            player.width = oldRect.Width;
                            player.height = oldRect.Height;
                        } else {                                // Sessions
                            char[] separator = { ' ' };
                            String[] words = value.Split(separator);
                            JSONSession sess = null;

                            if (words.Length == 8) {
                                // This shouldn't match anymore because we remove all sessions involving this player
                                foreach (JSONSession nxtSess in sessions) {
                                    if (key.Equals(nxtSess.id)) {
                                        sess = nxtSess;

                                        break;
                                    }
                                };

                                if (sess == null) {
                                    sess = new JSONSession();
                                    sess.id = key;
                                    sessions.Add(sess);
                                };
                                sess.srcId = words[0];
                                sess.sinkId = words[1];
                                try {
                                    sess.x = Convert.ToInt32(words[2]);
                                    sess.y = Convert.ToInt32(words[3]);
                                    sess.width = Convert.ToInt32(words[4]);
                                    sess.height = Convert.ToInt32(words[5]);
                                    sess.iconified = Convert.ToInt32(words[6]);
                                    sess.fullScreen = Convert.ToInt32(words[7]);
                                } catch (FormatException) {
                                    // Would rather have all correct sessions than partially correct sessions
                                    sessions.Remove(sess);
                                }

                                Trace.WriteLine("DEBUG: " + sess.id + " at " + sess.width + " x " + sess.height);

                            } else
                                Trace.WriteLine("FATAL: Unknown attribute " + key + ":" + value);
                        }
                        break;
                }
            }
        }
        #endregion

        #region Bonjour browser callback functions
        /// <summary>
        /// Bonjour callback
        /// </summary>
        /// <param name="service"></param>
        private void didUpdateTXT(NetService service) {
            ArrayList list = getList(service.Type);

            if (list == null)
                return;

            lock (list.SyncRoot) {
                foreach (JSONSrcSink player in list) {
                    if (player.id.Equals(service.Name)) {
                        processTXTrecord(service, player);

                        // Kludge. Sometimes, we miss the TXT update record from Bonjour. 
                        // So, we stop and restart a new monitoring session. That forces the system to continously get updates/stop/new update/stop, yuck
                        service.StopMonitoring();

                        service.DidUpdateTXT += new NetService.ServiceTXTUpdated(didUpdateTXT);
                        service.StartMonitoring();

                        return;
                    }
                }
            }
            
        }
     
        /// <summary>
        /// Bonjour callback. Remember resolved services and initiate monitoring TXT record updates
        /// </summary>
        /// <param name="service"></param>
        private void didResolvePlayers(NetService service) {
            JSONSrcSink newPlayer = new JSONSrcSink();

            newPlayer.id = service.Name;
            if (service.TXTRecordData != null)
                processTXTrecord(service, newPlayer);

            // Remove any previous remembered entries
            ArrayList list = getList(service.Type);
            lock (list.SyncRoot) {
                ArrayList toRemove = new ArrayList();

                foreach (JSONSrcSink item in list) {
                    if (item.id.Equals(service.Name))
                        toRemove.Add(item);
                }
                if (toRemove.Count > 0) {
                    foreach (JSONSrcSink item in toRemove)
                        list.Remove(item);
                    toRemove.Clear();
                }

                list.Add(newPlayer);
            }

            // Store the service in either sink or sources.
            ArrayList services = null;
            if (service.Type.StartsWith(Shared.DisplayCastGlobals.PLAYER) || service.Type.StartsWith(Shared.DisplayCastGlobals.ARCHIVER))
                services = sinkServices;

            if (service.Type.StartsWith(Shared.DisplayCastGlobals.STREAMER))
                services = sourceServices;
            Debug.Assert(services != null);

            // First remove any old entries
            lock (services.SyncRoot) {
                ArrayList toRemove = new ArrayList();

                foreach (NetService item in services) {
                    if (item.Name.Equals(service.Name))
                        toRemove.Add(item);
                }
                if (toRemove.Count > 0) {
                    foreach (NetService item in toRemove)
                        services.Remove(item);
                    toRemove.Clear();
                }

                // Add the current service
                services.Add(service);
            }

            service.DidUpdateTXT += new NetService.ServiceTXTUpdated(didUpdateTXT);
            service.StartMonitoring();
        }

        /// <summary>
        /// Bonjour callback. We don't remember services until they are resolved
        /// </summary>
        /// <param name="browser"></param>
        /// <param name="service"></param>
        /// <param name="moreComing"></param>
        private void didFindPlayers(NetServiceBrowser browser, NetService service, bool moreComing) {
            service.DidResolveService += new NetService.ServiceResolved(didResolvePlayers);
            service.ResolveWithTimeout(5);
        }

        /// <summary>
        /// Bonjour callback - cleanup removed services
        /// </summary>
        /// <param name="browser"></param>
        /// <param name="service"></param>
        /// <param name="moreComing"></param>
        private void didRemovePlayers(NetServiceBrowser browser, NetService service, bool moreComing) {
            // First remove from list of known players/streamers/archivers
            ArrayList list = getList(service.Type);
            if (list == null)
                return;

            ArrayList itemsToRemove = new ArrayList();
            lock (list.SyncRoot) {
                foreach (JSONSrcSink player in list) {
                    if (player.id.Equals(service.Name)) {
                        itemsToRemove.Add(player);
                        break;
                    }
                }
                if (itemsToRemove.Count > 0) {
                    foreach (JSONSrcSink player in itemsToRemove)
                        list.Remove(player);
                    itemsToRemove.Clear();
                    return;
                }
            }

            // now remove the services
            if (service.Type.StartsWith(Shared.DisplayCastGlobals.PLAYER) || service.Type.StartsWith(Shared.DisplayCastGlobals.ARCHIVER))
                lock(sinkServices.SyncRoot)
                    sinkServices.Remove(service);
            if (service.Type.StartsWith(Shared.DisplayCastGlobals.STREAMER))
                lock (sourceServices.SyncRoot)
                    sourceServices.Remove(service);
            
            service.Stop();
        }
        #endregion

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="sessions">Array of sessions: shared with API APIresponder</param>
        /// <param name="players">Array of players: shared with API APIresponder</param>
        /// <param name="streamers">Array of streamers: shared with API APIresponder</param>
        /// <param name="archivers">Array of archivers: shared with API APIresponder</param>
        /// <param name="sinkServices">Array of netservice sinks: shared with API APIresponder</param>
        /// <param name="sourceServices">Array of netservice sources: shared with API APIresponder</param>
        public monitorPlayers(ArrayList sessions, ArrayList players, ArrayList streamers, ArrayList archivers, ArrayList sinkServices, ArrayList sourceServices) {
            this.sessions = sessions;
            this.players = players;
            this.streamers = streamers;
            this.archivers = archivers;
            this.sinkServices = sinkServices;
            this.sourceServices = sourceServices;

            // Start browsing for Players/Streamers and Archivers
            playerBrowser = new NetServiceBrowser();
            playerBrowser.AllowMultithreadedCallbacks = true;
            playerBrowser.DidFindService += new NetServiceBrowser.ServiceFound(didFindPlayers);
            playerBrowser.DidRemoveService += new NetServiceBrowser.ServiceRemoved(didRemovePlayers);
            playerBrowser.SearchForService(Shared.DisplayCastGlobals.PLAYER, Shared.DisplayCastGlobals.BONJOURDOMAIN);

            streamerBrowser = new NetServiceBrowser();
            streamerBrowser.AllowMultithreadedCallbacks = true;
            streamerBrowser.DidFindService += new NetServiceBrowser.ServiceFound(didFindPlayers);
            streamerBrowser.DidRemoveService += new NetServiceBrowser.ServiceRemoved(didRemovePlayers);
            streamerBrowser.SearchForService(Shared.DisplayCastGlobals.STREAMER, Shared.DisplayCastGlobals.BONJOURDOMAIN);

            archiveBrowser = new NetServiceBrowser();
            archiveBrowser.AllowMultithreadedCallbacks = true;
            archiveBrowser.DidFindService += new NetServiceBrowser.ServiceFound(didFindPlayers);
            archiveBrowser.DidRemoveService += new NetServiceBrowser.ServiceRemoved(didRemovePlayers);
            archiveBrowser.SearchForService(Shared.DisplayCastGlobals.ARCHIVER, Shared.DisplayCastGlobals.BONJOURDOMAIN);
        }
    }
}
