// Copyright (c) 2012, Fuji Xerox Co., Ltd.
// All rights reserved.
// Author: Surendar Chandra, FX Palo Alto Laboratory, Inc.

using System;
using System.Collections.Generic;
using System.Linq;
using System.ServiceProcess;
using System.Text;

namespace FXPAL.DisplayCast.ControllerService {
    static class Program {
        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        static void Main() {
#if CONTROLLER_DEBUG_SERVICE
            Service s = new Service();
            s.Start();
#else
            ServiceBase[] ServicesToRun;
            ServicesToRun = new ServiceBase[] { 
				new Service() 
			};
            ServiceBase.Run(ServicesToRun);
#endif
        }
    }
}
