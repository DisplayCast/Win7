// Copyright (c) 2012, Fuji Xerox Co., Ltd.
// All rights reserved.
// Author: Surendar Chandra, FX Palo Alto Laboratory, Inc.

using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;
using System.Collections;
using System.Threading;
using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Windows.Forms;

using location;

namespace Location {
    public class LocationServices {
        public static QueryMSE MSE = null;
        public static ArrayList myMacs;

        public ArrayList getWIFIMACAddresses() {
            NetworkInterface[] nics = NetworkInterface.GetAllNetworkInterfaces();
            ArrayList nicStrings = new ArrayList();

            if (nics == null || nics.Length < 1)
                return nicStrings;
            
            foreach (NetworkInterface adapter in nics) {
                if (adapter.NetworkInterfaceType != NetworkInterfaceType.Wireless80211)
                    continue;

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
                nicStrings.Add(mac);
                Trace.WriteLine(" " + mac);
            }

            return nicStrings;
        }

        public LocationServices() {
            MSE = new QueryMSE();
            myMacs = getWIFIMACAddresses();
            if (myMacs.Count == 0)
                myMacs.Add("00:00:00:00:00:00");
        }

        public static void monitorMyLocation(object o, System.EventArgs evt) {
          TextBox locationString = (TextBox) o;
          if (locationString.GetType() != typeof(TextBox)) {
              Trace.WriteLine("Hmmm, not the right type");
              return;
          }
            
          while (true) {
                try {
                    MSE.login();
                    AesMobileStationLocation[] locs = MSE.query();
                    MSE.logout();
                              
                    foreach (AesMobileStationLocation loc in locs) {
                        foreach (String mac in myMacs) {
                            if (loc.macAddress.Equals(mac)) {
                                Trace.WriteLine(" Mac: " + loc.macAddress + " Loc: " + loc.x + "x" + loc.y + " lastHeard " + loc.minLastHeardSecs + " conf " + loc.confidenceFactor);
                            }
                        }
                    }
                } catch {
                }
            }
        }
    }
}
