// Copyright (c) 2012, Fuji Xerox Co., Ltd.
// All rights reserved.
// Author: Surendar Chandra, FX Palo Alto Laboratory, Inc.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Net;

using aaa;
using location;
using System.Diagnostics;

namespace Location {
    public class QueryMSE {
        public string mseAddr = "192.168.20.84";
        public string mseUser = "bigbrother";
        public string msePasswd = "bigbrother";

        public bool verbosity = false;

        int sessionId;

        public static bool ValidateServerCertificate(object sender, System.Security.Cryptography.X509Certificates.X509Certificate certificate, System.Security.Cryptography.X509Certificates.X509Chain chain, System.Net.Security.SslPolicyErrors sslPolicyErrors) {
            return true;
        }

        public bool login() {
            aaaService service = new aaaService("https://" + mseAddr + "/aaa/");
            //Login 
            LoginMethodArgs LM = new LoginMethodArgs();
            AesLogin AL = new AesLogin();
            AL.userName = mseUser;
            AL.password = msePasswd;
            LM.AesLogin = AL;
            try {
                Session s = service.Login(LM);
                if (s != null) {
                    aaa.AesBusinessSession AB = (aaa.AesBusinessSession)s.AesBusinessSession;

                    if (verbosity)
                        Trace.WriteLine("\nConnection ID = " + AB.id);
                    sessionId = AB.id;
                    return true;
                }
            } catch (Exception x) {
                Trace.WriteLine(x);
                return false;
            }
            return false;
        }

        public bool logout() {
            try {
                aaaService service = new aaaService("https://" + mseAddr + "/aaa/");
                LogoutMethodArgs LMA = new LogoutMethodArgs();
                LMA.AesBusinessSession = new aaa.AesBusinessSession();
                LMA.AesBusinessSession.id = sessionId;
                LMA.AesLogout = new AesLogout();
                aaa.Response r2 = service.Logout(LMA);
                if (r2 != null) {
                    return true;
                } else {
                    Trace.WriteLine("Logout failed");
                    return false;
                }
            } catch {
                return false;
            }
        }

        public void ping() {
            ServicePointManager.ServerCertificateValidationCallback += new System.Net.Security.RemoteCertificateValidationCallback(ValidateServerCertificate);

            LocationService LS = new LocationService("https://" + mseAddr + "/location/");
            try {
                PingMethodArgs PMA = new PingMethodArgs();
                PMA.AesBusinessSession = new location.AesBusinessSession();
                PMA.AesBusinessSession.id = sessionId;
                AesPing AP = new AesPing();
                PMA.AesPing = AP;
                location.Response r = LS.Ping(PMA);
            } catch (Exception x) {
                Trace.WriteLine(x);
            }

        }

        public AesMobileStationLocation[] query() {
            //ping();
            ServicePointManager.ServerCertificateValidationCallback += new System.Net.Security.RemoteCertificateValidationCallback(ValidateServerCertificate);

            LocationService LS = new LocationService("https://" + mseAddr + "/location/");
            try {
                GetChangesMethodArgs GCM = new GetChangesMethodArgs();
                GCM.AesBusinessSession = new location.AesBusinessSession();
                GCM.AesBusinessSession.id = sessionId;
                AesGetChanges AGC = new AesGetChanges();
                AGC.classname = "AesMobileStationLocation";

                GCM.AesGetChanges = AGC;
                location.Response r = LS.GetChanges(GCM);

                if (r != null && r.Items != null) {
                    int count = 0;
                    foreach (location.AesObject aesObj in r.Items) {
                        if (aesObj is AesMobileStationLocation)
                            count++;
                    }
                    AesMobileStationLocation[] locs = new AesMobileStationLocation[count];
                    int j = 0;
                    foreach (location.AesObject aesObj in r.Items) {
                        if (aesObj is AesMobileStationLocation) {
                            AesMobileStationLocation loc = (AesMobileStationLocation)aesObj;
                            locs[j++] = loc;
                        }
                    }
                    return locs;
                } else {
                    Trace.WriteLine("GetChanges Failed");
                }
            } catch (Exception x) {
                Trace.WriteLine(x);
            }
            return new location.AesMobileStationLocation[0];
        }

        public AesMobileStationLocation queryMAC(String mac) {
            ServicePointManager.ServerCertificateValidationCallback += new System.Net.Security.RemoteCertificateValidationCallback(ValidateServerCertificate);

            LocationService LS = new LocationService("https://" + mseAddr + "/location/");
            String lmac = mac.ToLower();
            try {
                AesGetChanges AGC = new AesGetChanges();
                AGC.classname = "AesMobileStationLocation";

                GetChangesMethodArgs GCM = new GetChangesMethodArgs();
                GCM.AesBusinessSession = new location.AesBusinessSession();
                GCM.AesBusinessSession.id = sessionId;
                GCM.AesGetChanges = AGC;

                location.Response r = LS.GetChanges(GCM);
                if ((r == null) || (r.Items == null))
                    return null;

                foreach (location.AesObject aesObj in r.Items) {
                    if (aesObj is AesMobileStationLocation) {
                        AesMobileStationLocation loc = (AesMobileStationLocation)aesObj;
                        // if (loc.macAddress.Equals(mac))
                        //    return (loc);
                        if (loc.macAddress.ToLower().Equals(lmac))
                            return (loc);
                    }
                }
            } catch (Exception x) {
                Trace.WriteLine(x);
            }
            return null;
        }
    }
}
