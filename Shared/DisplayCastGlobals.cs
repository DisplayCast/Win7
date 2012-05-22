// Copyright (c) 2012, Fuji Xerox Co., Ltd.
// All rights reserved.
// Author: Surendar Chandra, FX Palo Alto Laboratory, Inc.

// C# requires #defines to precede anything else. So, need to enter them in the compiler configuration. What a mess!!
        // #define USE_BLUETOOTH
        // #define USE_IONIC_ZLIB_N                // Supposedly better Zlib library
        // #define USE_WIFI_LOCALIZATION_N
        // #define PLAYER_TASKBAR
        // #define CONTROLLER_DEBUG_SERVICE_N      // Debugging services is a pain. define this to run the service as an application

// Configuration in settings: "USE_BITMAP_COMPRESS;USE_BLUETOOTH;USE_IONIC_ZLIB_N;USE_WIFI_LOCALIZATION_N;PLAYER_TASKBAR;CONTROLLER_DEBUG_SERVICE_N"

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Collections;

namespace Shared {
    /// <summary>
    /// Constants used by DisplayCast system. 
    /// </summary>
    public static class DisplayCastGlobals {
        /// <summary>
        /// Bonjour service names
        /// </summary>
        public const String STREAMER = "_dc-streamer._tcp";
        public const string ARCHIVER = "_dc-archiver._tcp";
        public const string PLAYER = "_dc-player._tcp";
        // public const string DOMAIN = "bonjour.fxpal.net";       // If you have wide area bonjour, enter that domain here
        public const string BONJOURDOMAIN = "";

        public const string STREAMER_CMD_SYNTAX_ERROR = "SYNTAX ERROR";

        public const string PLAYER_CMD_SYNTAX_ERROR = "SYNTAX ERROR";
        public const string PLAYER_CMD_SUCCESS = "SUCCESS";

        public const string PLAYER_USAGE_MOVE = "USAGE: MOVE ";

        // We hardcode to listen for HTTP/REST requests on port 11223
        public const string CONTROL_API_URL = "http://+:11223/";

        // Controller error strings
        public const string CONTROL_REMOTE_FAILED = "FATAL: Remote control failed for ";
        public const string CONTROL_REMOTE_IP_NOTFOUND = "FATAL: Remote control failed to locate usable IP end point for ";

        public const string CONTROL_USAGE_STATUS = "USAGE: status <id>";
        public const string CONTROL_USAGE_SESSIONSTATUS = "USAGE: sessionstatus <id>";
        public const string CONTROL_USAGE_SNAPSHOT = "USAGE: snapshot id=<id>";
        public const string CONTROL_USAGE_CONNECT = "USAGE: connect source=<id> sink=<id>";
        public const string CONTROL_USAGE_DISCONNECT = "USAGE: disconnect <id>";
        public const string CONTROL_USAGE_MOVE = "USAGE: move sessionId=<id> x= y= width= height=";
        public const string CONTROL_USAGE_ICON = "USAGE: icon <id>";
        public const string CONTROL_USAGE_DICO = "USAGE: dico <id>";
        public const string CONTROL_USAGE_FULLSCREEN = "USAGE: fullscreen <id>";
        public const string CONTROL_USAGE_MASK = "USAGE: mask streamerId=<id> x= y= width= height=";

        public const string CONTROL_JSON_SYNTAX_ERROR = "SYNTAX ERROR";
        public const string CONTROL_JSON_UNKNOWN_ERROR = "UNKNOWN ID ";
        public const string CONTROL_JSON_UNIMPL_ERROR = "NOT IMPLEMENTED";
 
        public const float DISPLAYCAST_VERSION = 1.1F;
    }
}
