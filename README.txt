Copyright (c) 2012, Fuji Xerox Co., Ltd.
All rights reserved.
Author: Surendar Chandra, FX Palo Alto Laboratory, Inc.
Contact: displaycast@fxpal.com

0.0  License: 
   DisplayCast is released under the New BSD license. Specific
   licensing terms are described in the License.rtf file. This license
   agreement applies to the entire DisplayCast system.

1.0 Introduction:

2.0 System Requirements:
    DisplayCast requires:
    	a) Apple Bonjour for naming and location management. Either
    the full SDK or the "Bonjour Print Services for Windows", both
    available at https://developer.apple.com/opensource/ will work.
    	b) Demoforge mirror driver, available at
    http://www.demoforge.com/dfmirage.htm.

3.0 Getting DisplayCast:
    3.1 Prebuilt binaries are available from the project web page at
    http://www.fxpal.com/?p=DisplayCast/.

    3.2 Source code is available in github at
    https://github.com/DisplayCast/DisplayCast-Win7

4.0 Build Intructions:
    We use Microsoft Visual Studio 2010. Visual Studio 2010 Express
    may also be used though the Express version does not support
    creating installers. 

    The source code for the various projects are available inside the
    "Sources" folder. Projects "Player" and "Streamer" create the
    corresponding DisplayCast executables. The project "Location" is
    an experimental feature that uses Cisco WiFi
    localization. "ControllerService" is a Windows service that
    listens to Bonjour services and provides a HTTP/REST service. By
    default, the service uses port 11223 and provides JSONP
    responses. "Shared" defines global parameters that are used by the
    entire system. "ZeroconfService" is the open source C# wrapper for
    Bonjour and is available at
    http://code.google.com/p/zeroconfignetservices/. You can download
    precompiled DLL from that link though I experienced some trouble
    in using the precompiled binaries.

    Installers can be built using the projects inside the Installers
    director. The projects "ControllerServiceInstaller" and
    "DisplayCastInstaller" create installers for the Controller
    Service and the DisplayCast system respectively.

5.0 Known Issues:

6.0 Frequently asked questions:
