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
using System.Net.NetworkInformation;
using System.Resources;
using System.Reflection;

using Microsoft.Win32;
using Microsoft.VisualBasic;

using ZeroconfService;
#if USE_WIFI_LOCALIZATION
using Location;
using location;
#endif 

using Mirror.Driver;

#if USE_BLUETOOTH
using InTheHand.Net;
using InTheHand.Net.Sockets;
using InTheHand.Windows.Forms;
using InTheHand.Net.Bluetooth;
#endif

namespace FXPAL.DisplayCast.Streamer {
    public partial class Console : Form {
        private NetService publishService;
        private Hashtable TXTrecords;
        private ContextMenu contextMenu;
        private NotifyIcon notifyIcon;
        private static MenuItem exitItem, changeNameItem, desktopItem, locationItem, aboutItem, playersItem, archiversItem;
        // private CheckBox location;
        // private ListView browserList;
        private int numPlayers = 0, numArchivers = 0;
        private String id;

        private ArrayList resolvingNS = null;

        #region Utility functions
        /// <summary>
        /// Gets the user configured name for a service from its TXTrecords
        /// </summary>
        /// <param name="service"></param>
        /// <returns></returns>
        private static String getName(NetService service) {
            String name = service.Name;

            if (service.TXTRecordData != null) {
                byte[] txt = service.TXTRecordData;
                IDictionary dict = NetService.DictionaryFromTXTRecordData(txt);

                if (dict != null) {
                    foreach (DictionaryEntry kvp in dict) {
                        String key = (String)kvp.Key;

                        key = key.ToUpper();
                        if (key.Equals("NAME")) {
                            byte[] value = (byte[])kvp.Value;
                            try {
                                name = Encoding.UTF8.GetString(value);
                            } catch {
                            }
                            break;
                        }
                    }
                }
            }

            return name;
        }
        #endregion

        #region Bonjour browse routines to keep track of Players
        /// <summary>
        /// 
        /// </summary>
        /// <param name="browser"></param>
        /// <param name="service"></param>
        /// <param name="moreComing"></param>
        void nsBrowser_DidRemoveService(NetServiceBrowser browser, NetService service, bool moreComing) {
            MenuItem items;
            Boolean player = false;

            if (service.Type.StartsWith(Shared.DisplayCastGlobals.PLAYER)) {
                items = playersItem;
                player = true;
            } else if (service.Type.StartsWith(Shared.DisplayCastGlobals.ARCHIVER))
                items = archiversItem;
            else
                return;

            Trace.WriteLine("DEBUG: Removing service " + service.Name + " of type " + service.Type);

            ArrayList itemsToRemove = new ArrayList();
            foreach (MenuItem item in items.MenuItems) {
                if (item == null)
                    continue;   // WTF?

                NetService iService = (NetService)item.Tag;

                if (resolvingNS.Contains(iService))
                    resolvingNS.Remove(iService);

                // Check for the separator item
                if (iService == null)
                    continue;

                if (iService.Name.Equals(service.Name)) {
                    itemsToRemove.Add(item);
                    // Dec 5, 2011
                    iService.StopMonitoring();
                    iService.Stop();

                    if (player)
                        numPlayers--;
                    else
                        numArchivers--;
                }
            }

            foreach (MenuItem item in itemsToRemove)
                items.MenuItems.Remove(item);
            itemsToRemove.Clear();
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="browser"></param>
        /// <param name="service"></param>
        /// <param name="moreComing"></param>
        void nsBrowser_DidFindService(NetServiceBrowser browser, NetService service, bool moreComing) {
            // Service found. Need to resolve it to find the name

            if (resolvingNS == null)
                resolvingNS = new ArrayList();
            resolvingNS.Add(service);   // So that service is not garbage collected away

            service.DidResolveService += new NetService.ServiceResolved(nsBrowser_DidResolveService);
            service.ResolveWithTimeout(5);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="service"></param>
        void nsBrowser_DidUpdateTXT(NetService service) {
            // There might be duplicates and so go through the entire list
            // Trace.WriteLine("DEBUG: TXT updates " + service.Name);
            MenuItem items;

            if (service.Type.StartsWith(Shared.DisplayCastGlobals.PLAYER))
                items = playersItem;
            else if (service.Type.StartsWith(Shared.DisplayCastGlobals.ARCHIVER))
                items = archiversItem;
            else
                return;

            foreach (MenuItem item in items.MenuItems) {
                if (item == null)
                    continue;

                NetService iService = (NetService)item.Tag;

                // Check for the separator item
                if (iService == null)
                    continue;

                if (iService.Name.Equals(service.Name))
                    // I suppose I can blindly replace the old text
                    item.Text = getName(service);
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="service"></param>
        public void nsBrowser_DidResolveService(NetService service) {
            // Start monitoring to see whether the name changes
            service.DidUpdateTXT += new NetService.ServiceTXTUpdated(nsBrowser_DidUpdateTXT);
            service.StartMonitoring();
          
            MenuItem newElem = new System.Windows.Forms.MenuItem();
            newElem.Text = getName(service);
            newElem.Click += new System.EventHandler(selectPlayer);
            newElem.Select += new System.EventHandler(selectPlayer);
            newElem.Tag = service;

            if (service.Type.StartsWith(Shared.DisplayCastGlobals.PLAYER)) {
                newElem.Index = numPlayers++;
                playersItem.MenuItems.Add(newElem);
            } else if (service.Type.StartsWith(Shared.DisplayCastGlobals.ARCHIVER)) {
                newElem.Index = numArchivers++;
                archiversItem.MenuItems.Add(newElem);
            }
        }
        #endregion

#if USE_WIFI_LOCALIZATION
        #region Location disclosure support
        private static Boolean discloseLocation = false;

        static QueryMSE MSE;
        static String myMac;
        static Thread locThread;
        static AesMobileStationLocation prevLoc = null;
        private String getWIFIMACAddress() {
            NetworkInterface[] nics = NetworkInterface.GetAllNetworkInterfaces();

            if (nics == null || nics.Length < 1)
                return null;

            foreach (NetworkInterface adapter in nics) {
                if (adapter.NetworkInterfaceType != NetworkInterfaceType.Wireless80211)
                    continue;

                if (adapter.Description.Contains("Virtual"))
                    continue;       // WTF - Displaycast desktop has a virtual WiFi adapter

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
                return (mac);
            }

            return null;
        }

        private void monitorMyLocation() {
            try {
                while (true) {
                    if (discloseLocation) {
                        Trace.WriteLine("DEBUG: Looking up location info..");
                        try {
                            MSE.login();
                            AesMobileStationLocation loc = MSE.queryMAC(myMac);
                            MSE.logout();

                            if (loc != null) {
                                if ((prevLoc == null) || (prevLoc.x != loc.x) || (prevLoc.y != loc.y)) {
                                    TXTrecords.Add("locationID", "NOTIMPL" /* loc.x + " x " + loc.y */);
                                    publishService.setTXTRecordData(NetService.DataFromTXTRecordDictionary(TXTrecords));

                                    Trace.WriteLine(" Mac: " + loc.macAddress + " Loc: " + loc.x + "x" + loc.y + " lastHeard " + loc.minLastHeardSecs + " conf " + loc.confidenceFactor);
                                    prevLoc = loc;
                                }
                            }
                        } catch (Exception e) {
                            Trace.WriteLine("DEBUG: Disclose location: " + e.StackTrace);
                        }
                    }
                    Thread.Sleep(5000);
                }
            } catch (Exception e) {
                Trace.WriteLine("Oops - " + e.Message);
            }
        }
        #endregion
#endif 

        #region Event handlers
        /// <summary>
        /// 
        /// </summary>
        /// <param name="Sender"></param>
        /// <param name="e"></param>
        private void about(object Sender, EventArgs e) {
            // var result = MessageBox.Show("FXPAL DisplayCast Streamer (v " + Shared.DisplayCastGlobals.DISPLAYCAST_VERSION.ToString() + "))", "FXPAL DisplayCast Streamer", MessageBoxButtons.OK);
            AboutBox about = new AboutBox();
            about.ShowDialog();
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="Sender"></param>
        /// <param name="e"></param>
        private void notifyIcon_DoubleClick(object Sender, EventArgs e) {
            if (this.WindowState == FormWindowState.Minimized)
                this.WindowState = FormWindowState.Normal;

            Activate();
        }

#region Set Primary Display
        public const int DM_ORIENTATION = 0x00000001;
        public const int DM_PAPERSIZE = 0x00000002;
        public const int DM_PAPERLENGTH = 0x00000004;
        public const int DM_PAPERWIDTH = 0x00000008;
        public const int DM_SCALE = 0x00000010;
        public const int DM_POSITION = 0x00000020;
        public const int DM_NUP = 0x00000040;
        public const int DM_DISPLAYORIENTATION = 0x00000080;
        public const int DM_COPIES = 0x00000100;
        public const int DM_DEFAULTSOURCE = 0x00000200;
        public const int DM_PRINTQUALITY = 0x00000400;
        public const int DM_COLOR = 0x00000800;
        public const int DM_DUPLEX = 0x00001000;
        public const int DM_YRESOLUTION = 0x00002000;
        public const int DM_TTOPTION = 0x00004000;
        public const int DM_COLLATE = 0x00008000;
        public const int DM_FORMNAME = 0x00010000;
        public const int DM_LOGPIXELS = 0x00020000;
        public const int DM_BITSPERPEL = 0x00040000;
        public const int DM_PELSWIDTH = 0x00080000;
        public const int DM_PELSHEIGHT = 0x00100000;
        public const int DM_DISPLAYFLAGS = 0x00200000;
        public const int DM_DISPLAYFREQUENCY = 0x00400000;
        public const int DM_ICMMETHOD = 0x00800000;
        public const int DM_ICMINTENT = 0x01000000;
        public const int DM_MEDIATYPE = 0x02000000;
        public const int DM_DITHERTYPE = 0x04000000;
        public const int DM_PANNINGWIDTH = 0x08000000;
        public const int DM_PANNINGHEIGHT = 0x10000000;
        public const int DM_DISPLAYFIXEDOUTPUT = 0x20000000;
        public const int ENUM_CURRENT_SETTINGS = -1;
        public const int CDS_UPDATEREGISTRY = 0x01;
        public const int CDS_TEST = 0x02;
        public const int CDS_SET_PRIMARY = 0x00000010;
        public const long DISP_CHANGE_SUCCESSFUL = 0;
        public const long DISP_CHANGE_RESTART = 1;
        public const long DISP_CHANGE_FAILED = -1;
        public const long DISP_CHANGE_BADMODE = -2;
        public const long DISP_CHANGE_NOTUPDATED = -3;
        public const long DISP_CHANGE_BADFLAGS = -4;
        public const long DISP_CHANGE_BADPARAM = -5;
        public const long DISP_CHANGE_BADDUALVIEW = -6;

        /// <summary>
        /// 
        /// </summary>
        /// <param name="screen"></param>
        private static void SetPrimary(Screen screen) {
        /*
            DISPLAY_DEVICE d = new DISPLAY_DEVICE();
            DEVMODE dm = new DEVMODE();
            d.cb = Marshal.SizeOf(d);
            uint deviceID = 1;
            User_32.EnumDisplayDevices(null, deviceID, ref  d, 0);
            User_32.EnumDisplaySettings(d.DeviceName, 0, ref dm);
            dm.dmPelsWidth = 2560;
            dm.dmPelsHeight = 1600;
            dm.dmPositionX = screen.Bounds.Right;
            dm.dmFields = DM_POSITION | DM_PELSWIDTH | DM_PELSHEIGHT;
            User_32.ChangeDisplaySettingsEx(d.DeviceName, ref dm, IntPtr.Zero, CDS_SET_PRIMARY, IntPtr.Zero);
         */
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="Sender"></param>
        /// <param name="e"></param>
        private void selectDesktop(object Sender, EventArgs e) {
            MenuItem selected = (MenuItem)Sender;
            Screen screen = (Screen)selected.Tag;

            SetPrimary(screen);
        }
#endregion

        /// <summary>
        /// Menu callback to send to Player
        /// </summary>
        /// <param name="Sender"></param>
        /// <param name="e"></param>
        private void selectPlayer(object Sender, EventArgs e) {
            MenuItem clicked = (MenuItem)Sender;
            NetService service = (NetService)clicked.Tag;
            IList addresses = service.Addresses;
            byte[] bytes;

            foreach (System.Net.IPEndPoint addr in addresses) {
                System.Net.Sockets.TcpClient clntSocket = new System.Net.Sockets.TcpClient();
                try {
                    clntSocket.Connect(addr);
                    if (clntSocket.Connected) {
                        Trace.WriteLine("DEBUG: Connected to " + service.Name);
                        NetworkStream strm = clntSocket.GetStream();

                        if (clicked.Checked) {
                            String handle = clicked.Name;

                            bytes = Encoding.ASCII.GetBytes("CLOSE " + handle + "\n");
                            strm.Write(bytes, 0, bytes.Length);

                            Trace.WriteLine("CLOSING : " + handle);
                            bytes = new byte[1024];
                            // strm.ReadTimeout = 5000;
                            try {
                                strm.Read(bytes, 0, bytes.Length);

                                Trace.WriteLine("Returned " + System.Text.Encoding.ASCII.GetString(bytes));
                            } catch (Exception ex) {
                                Trace.WriteLine("FATAL: " + ex.StackTrace);
                            }
                            clicked.Checked = false;
                        } else {
                            bytes = Encoding.ASCII.GetBytes("SHOW " + id + " FULLSCREEN\n");
                            strm.Write(bytes, 0, bytes.Length);

                            bytes = new byte[128];
                            strm.ReadTimeout = 5000;
                            try {
                                strm.Read(bytes, 0, bytes.Length);
                                clicked.Checked = true;
                                clicked.Name = System.Text.Encoding.ASCII.GetString(bytes);
                            } catch {
                            }
                        }
                        strm.Close();   // The result handle have no use for us

                        break;
                    }
                } catch {
                }
            }
        }

#if USE_WIFI_LOCALIZATION
        private void locationChecked(object Sender, EventArgs e) {
            if (locationItem.Checked) {
                locationItem.Checked = false;

                prevLoc = null;
                TXTrecords.Remove("locationID");
                publishService.setTXTRecordData(NetService.DataFromTXTRecordDictionary(TXTrecords));
            } else {
                locationItem.Checked = true;
                discloseLocation = true;
            }
            using (RegistryKey dcs = Registry.CurrentUser.CreateSubKey("Software").CreateSubKey("FXPAL").CreateSubKey("DisplayCast").CreateSubKey("Streamer")) {
                dcs.SetValue("discloseLocation", locationItem.Checked);
            }
        }
#endif

        /// <summary>
        /// 
        /// </summary>
        /// <param name="Sender"></param>
        /// <param name="e"></param>
        private void exitItem_Click(object Sender, EventArgs e) {
            Environment.Exit(0);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Console_Load(object sender, EventArgs e) {
            /*
            using (RegistryKey dcs = Registry.CurrentUser.CreateSubKey("Software").CreateSubKey("FXPAL").CreateSubKey("DisplayCast").CreateSubKey("Streamer")) {
                streamerName.Clear();
                streamerName.AppendText(dcs.GetValue("Name").ToString());
            }
             */
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void changeName(object sender, EventArgs e) {
            if (publishService != null) {
                String defaultName;

                using (RegistryKey dcs = Registry.CurrentUser.CreateSubKey("Software").CreateSubKey("FXPAL").CreateSubKey("DisplayCast").CreateSubKey("Streamer")) {
                    defaultName = dcs.GetValue("Name", System.Environment.UserName).ToString();
                    String name = Interaction.InputBox("Enter our name. Remote players will use this name to watch our screencast.", "FXPal DisplayCast Streamer", defaultName, 0, 0);

                    if (name.Length != 0) {
                        dcs.SetValue("Name", name);
                        // Trace.WriteLine("DEBUG - name is " + name + " length is " + name.Length);

                        TXTrecords.Remove("name");
                        TXTrecords.Add("name", name);
                        publishService.TXTRecordData = NetService.DataFromTXTRecordDictionary(TXTrecords);
                        publishService.TXTRecordData = NetService.DataFromTXTRecordDictionary(TXTrecords);
                        publishService.Publish();
                    }
                }
            }
        }
        #endregion

#if USE_BLUETOOTH
        #region Bluetooth stuff
        static BluetoothComponent bt;

        /// <summary>
        /// Callback on a completed discover operation
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        static private void discoverComplete(object sender, DiscoverDevicesEventArgs e) {
            BluetoothDeviceInfo[] devices = e.Devices;

            foreach (MenuItem menu in playersItem.MenuItems) {
                if (menu == null)
                    continue;   // WTF?

                if (menu.Text.EndsWith("(nearby)"))
                    menu.Text = menu.Text.Replace("(nearby)", "");
            }

            try {
                foreach (BluetoothDeviceInfo device in e.Devices) {
                    string addr = device.DeviceAddress.ToString("C").Replace(":", "-").ToLower();

                    foreach (MenuItem menu in playersItem.MenuItems) {
                        if (menu == null)
                            continue;   // WTF?

                        NetService service = (NetService)menu.Tag;
                        if (service == null)
                            continue;   // WTF
                        if (service.TXTRecordData != null) {
                            IDictionary dict = NetService.DictionaryFromTXTRecordData(service.TXTRecordData);

                            if (dict != null) {
                                foreach (DictionaryEntry kvp in dict) {
                                    String key = (String)kvp.Key;

                                    key = key.ToUpper();
                                    if (key.Equals("BLUETOOTH")) {
                                        byte[] value = (byte[])kvp.Value;
                                        try {
                                            if (Encoding.UTF8.GetString(value).ToLower().Equals(addr)) {
                                                menu.Text = menu.Text + "(nearby)";
                                            }
                                        } catch {
                                        }
                                        break;
                                    }
                                }
                            }
                        }
                    }
                }

                BluetoothComponent bt = (BluetoothComponent)sender;
                bt.DiscoverDevicesAsync(100, true, true, true, true, null);
            } catch {
            }
        }
        #endregion
#endif

        private IContainer iComponents;
        private NetServiceBrowser player = null, archiver = null;
        /// <summary>
        /// 
        /// </summary>
        /// <param name="publishService"></param>
        /// <param name="TXTrecords"></param>
        /// <param name="id"></param>
        public Console(NetService publishService, Hashtable TXTrecords, String id) {
            InitializeComponent();

#if USE_BLUETOOTH
            // If Bluetooth is supported, then scan for nearby players
            if (BluetoothRadio.IsSupported) {
                try {
                    bt = new BluetoothComponent();

                    bt.DiscoverDevicesComplete += new EventHandler<DiscoverDevicesEventArgs>(discoverComplete);
                    bt.DiscoverDevicesAsync(100, true, true, true, true, null);
                } catch {
                    if (bt != null)
                        bt.Dispose();
                    // Something is wrong with bluetooth module
                }
            }
#endif

            this.publishService = publishService;
            this.TXTrecords = TXTrecords;
            this.id = id;

            playersItem = new System.Windows.Forms.MenuItem();
            playersItem.Text = "P&roject Me";
            playersItem.MenuItems.Add("-");

            archiversItem = new System.Windows.Forms.MenuItem();
            archiversItem.Text = "A&rchive Me";
            archiversItem.MenuItems.Add("-");

#if USE_WIFI_LOCALIZATION
            MSE = new QueryMSE();
            myMac = getWIFIMACAddress();
            /* if (myMac == null)
                myMac = displaycast; */
            if (myMac != null) {
                locationItem = new System.Windows.Forms.MenuItem();
                locationItem.Text = "Disclose location?";
                // locationItem.Index = 4;
                // playersItem.Index++;
                // archiversItem.Index++;
                locationItem.Click += new System.EventHandler(locationChecked);
                using (RegistryKey dcs = Registry.CurrentUser.CreateSubKey("Software").CreateSubKey("FXPAL").CreateSubKey("DisplayCast").CreateSubKey("Streamer")) {
                    locationItem.Checked = Convert.ToBoolean(dcs.GetValue("discloseLocation"));
                }
                discloseLocation = locationItem.Checked;

                locThread = new Thread(new ThreadStart(monitorMyLocation));
                locThread.Start();
            }
#endif

            Screen[] screens = Screen.AllScreens;
            if (screens.Length > 1) {
                desktopItem = new System.Windows.Forms.MenuItem();
                desktopItem.Text = "D&esktop to stream";

                desktopItem.MenuItems.Add("-");
                foreach (Screen screen in screens) {
                    MenuItem item = new MenuItem();
                    System.Drawing.Rectangle bounds = screen.Bounds;

                    item.Text = "DESKTOP: " + bounds.X + "x" + bounds.Y + " " + bounds.Width + "x" + bounds.Height;
                    item.Click += new System.EventHandler(selectDesktop);
                    item.Select += new System.EventHandler(selectDesktop);
                    item.Tag = screen;

                    desktopItem.MenuItems.Add(item);
                }
            }

            changeNameItem = new System.Windows.Forms.MenuItem();
            changeNameItem.Text = "C&hange Name";
            changeNameItem.Click += new System.EventHandler(changeName);

            aboutItem = new System.Windows.Forms.MenuItem();
            aboutItem.Text = "A&bout...";
            aboutItem.Click += new System.EventHandler(about);

            exitItem = new System.Windows.Forms.MenuItem();
            exitItem.Text = "E&xit";
            exitItem.Click += new System.EventHandler(exitItem_Click);

            contextMenu = new System.Windows.Forms.ContextMenu();

            contextMenu.MenuItems.Add(playersItem);
            contextMenu.MenuItems.Add(archiversItem);
            contextMenu.MenuItems.Add("-");

            if (locationItem != null)
                contextMenu.MenuItems.Add(locationItem);

            if (desktopItem != null)
                contextMenu.MenuItems.Add(desktopItem);
            contextMenu.MenuItems.Add(changeNameItem);

            contextMenu.MenuItems.Add("-");
            contextMenu.MenuItems.Add(aboutItem);

            contextMenu.MenuItems.Add("-");
            contextMenu.MenuItems.Add(exitItem);

            iComponents = new System.ComponentModel.Container();
            notifyIcon = new NotifyIcon(iComponents);
            notifyIcon.Icon = new Icon("Streamer.ico");
            // notifyIcon.Icon = Streamer.Properties.Resources.Streamer;
            // notifyIcon.Icon = new Icon(System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("FXPAL.DisplayCast.Streamer.Streamer.ico"));

            notifyIcon.ContextMenu = contextMenu;
            notifyIcon.Text = "FXPAL Displaycast Streamer";
            notifyIcon.Visible = true;
            notifyIcon.DoubleClick += new System.EventHandler(this.notifyIcon_DoubleClick);

            player = new NetServiceBrowser();
            //player.InvokeableObject = this;
            player.AllowMultithreadedCallbacks = true;
            player.DidFindService += new NetServiceBrowser.ServiceFound(nsBrowser_DidFindService);
            player.DidRemoveService += new NetServiceBrowser.ServiceRemoved(nsBrowser_DidRemoveService);
            player.SearchForService(Shared.DisplayCastGlobals.PLAYER, Shared.DisplayCastGlobals.BONJOURDOMAIN);

            archiver = new NetServiceBrowser();
            // archiver.InvokeableObject = this;
            archiver.AllowMultithreadedCallbacks = true;
            archiver.DidFindService += new NetServiceBrowser.ServiceFound(nsBrowser_DidFindService);
            archiver.DidRemoveService += new NetServiceBrowser.ServiceRemoved(nsBrowser_DidRemoveService);
            archiver.SearchForService(Shared.DisplayCastGlobals.ARCHIVER, Shared.DisplayCastGlobals.BONJOURDOMAIN);
        }
    }
}
