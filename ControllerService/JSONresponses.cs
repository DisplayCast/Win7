// Copyright (c) 2012, Fuji Xerox Co., Ltd.
// All rights reserved.
// Author: Surendar Chandra, FX Palo Alto Laboratory, Inc.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace FXPAL.DisplayCast.ControllerService {
    // Responses sent to end users as JSON objects
    /// <summary>
    /// Structure for sources (Streamer) and Sinks (Players, Archivers)
    /// </summary>
    class JSONSrcSink {
        public String id;           // Immutable ID. 
        public String description;      // User defined name. Names are used by users and can change at any time 
        public int x, y, width, height; // Dimensions in pixels
        public int maskX, maskY, maskWidth, maskHeight;  // The API now sends out both the actual screen dimensions (above) and the masked region
        public String locationID;   // Must be sent to the location server for resolution

        public String os;           // Just in case
        public String machineName;  // Just in case
        public String userName;     // User name for better sorting of Streamers
        public String nearBy;       // Currently uses BlueTooth to locate nearby players
        // public Double version;      // Make sure that we are talking to the right person
        // public int imagePort;
    }

    /// <summary>
    /// Sessions. They are reported by Players/Archivers
    /// </summary>
    class JSONSession {
        public String id;       // Immutable ID.
        public String srcId;    // JSONSrcSrink.id
        public String sinkId;   // JSONSrcSrink.id
        public int x, y, width, height;  // Location on sink
        public int iconified; // window state
        public int fullScreen; // window state
    }

    /// <summary>
    /// One a successful session creation, return the new session ID
    /// </summary>
    class JSONnewSession {
        public String id;       // Immutable ID.
    }

    /// <summary>
    /// Return the status of the operation
    /// </summary>
    class JSONstatus {
        public String result;
    }

    class JSONwhoami {
        public String player;   // Return the Player running at the IP address which sends a WHOAMI command
        public String streamer; // Returns the Stream
        public String archiver; // Returns the Archiver
    }
}
