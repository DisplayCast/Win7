// Copyright (c) 2012, Fuji Xerox Co., Ltd.
// All rights reserved.
// Author: Surendar Chandra, FX Palo Alto Laboratory, Inc.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Collections;
using System.Threading;
using System.Web.Script.Serialization;
using System.Diagnostics;
using System.Drawing.Imaging;
using System.Drawing;
using System.Drawing.Drawing2D;

using Shared;

using ZeroconfService;

// Functions that JSONP responses for the requests made to the API as a HTTP/REST call
namespace FXPAL.DisplayCast.ControllerService {
    class APIresponder {
        public HttpListener listener = new HttpListener();

        // Keep track of active sessions (reported by the player), players/streamers/archives as well as the sources and sink NetService
        private ArrayList sessions, players, streamers, archivers, sinkServices, sourceServices;
        private JavaScriptSerializer serializer = new JavaScriptSerializer();

        #region Utility functions to pass along commands to the actual entities (Player/Streamer)
        /// <summary>
        /// Utility function to send the command in 'cmd' to the bonjour 'service'. 
        /// This variant will use the addressses and port information that is advertised in 'service'.
        /// </summary>
        /// <param name="service">The Bonjour service that is expecting this command</param>
        /// <param name="cmd">A telnet like command interface</param>
        /// <returns>Returns the error message returned from this 'service' request</returns>
        private String sendCommand(NetService service, String cmd) {
            return sendCommand(service, -1, cmd);
        }

        /// <summary>
        /// Utility function to send the command to the network address specified by service but to the port specified by the parameter.  
        /// Used to send 'MASK' command to Streamer (which sends the actual data in the port specified by 'service')
        /// </summary>
        /// <param name="service"></param>
        /// <param name="port"></param>
        /// <param name="cmd"></param>
        /// <returns></returns>
        private String sendCommand(NetService service, Int32 port, String cmd) {
        // Bonjour hosts can be multihomed. Try all advertised addresses to see which one is appropriate for us
            IList addresses = service.Addresses;

            foreach (System.Net.IPEndPoint addr in addresses) {
                System.Net.Sockets.TcpClient clntSocket = new System.Net.Sockets.TcpClient();
            
                if (port != -1)
                    addr.Port = port;
                
                try {
                    clntSocket.Connect(addr);
                    if (clntSocket.Connected) {
                        Trace.WriteLine("DEBUG: Connected to " + service.Name);

                        try {
                            NetworkStream strm = clntSocket.GetStream();
                            byte[] bytes = Encoding.ASCII.GetBytes(cmd);
                            strm.Write(bytes, 0, bytes.Length);

                            bytes = new byte[1024];
                            int readLength = strm.Read(bytes, 0, bytes.Length);
                            return System.Text.Encoding.ASCII.GetString(bytes, 0, readLength);
                        } catch {
                            return DisplayCastGlobals.CONTROL_REMOTE_FAILED + service.Name;
                        }
                    }
                } catch (IOException) {
                    //Ignore and try the next address
                }
            }
            return DisplayCastGlobals.CONTROL_REMOTE_IP_NOTFOUND + service.Name;
        }

        /// <summary>
        /// Utility function to wrap the error message into a JSON message.
        /// </summary>
        /// <param name="err"></param>
        /// <returns></returns>
        private String JSONError(String err) {
            JSONstatus status = new JSONstatus();
            status.result = err;

            return this.serializer.Serialize(status);
        }
        #endregion

        /// <summary>
        /// Callback for ControlAPI HTTP/REST requests
        /// </summary>
        /// <param name="result"></param>
        protected void WebRequestCallback(IAsyncResult result) {
            // Sanity check though this can't possibly be true
            if (this.listener == null)
                return;

            // Remove this request and schedule for receiving future requests
            HttpListenerContext context = this.listener.EndGetContext(result);
            this.listener.BeginGetContext(new AsyncCallback(WebRequestCallback), this.listener);

            // Process current request
            this.ProcessRequest(context);
        }

        /// <summary>
        /// Process the current HTTP/REST request
        /// </summary>
        /// <param name="Context">returned from the asynchronous httplistener</param>
        protected virtual void ProcessRequest(HttpListenerContext Context) {
            HttpListenerRequest request = Context.Request;
            HttpListenerResponse response = Context.Response;
            String cmd = request.Url.LocalPath.Substring(1).ToUpper();
            String responseString = null;

            response.StatusCode = (int)HttpStatusCode.OK;

            // Giant command processing switch
            switch (cmd) {
                case "WHOAMI":
                    IPAddress who =  request.RemoteEndPoint.Address;
                    JSONwhoami ami = new JSONwhoami();

                    Trace.WriteLine("DEBUG: I am " + who.ToString() + " " + request.Headers);
                    foreach (NetService service in sinkServices) {
                        IList addresses = service.Addresses;

                        foreach (IPEndPoint addr in addresses) {
                            Trace.WriteLine("DEBUG: am I sink " + addr.Address.ToString());
                            if (who.Equals(addr.Address)) {
                                if (service.Type.StartsWith(Shared.DisplayCastGlobals.PLAYER))
                                    ami.player = service.Name;
                                if (service.Type.StartsWith(Shared.DisplayCastGlobals.ARCHIVER))
                                    ami.archiver = service.Name;
                            }
                        }
                    }

                    foreach (NetService service in sourceServices) {
                        IList addresses = service.Addresses;

                        foreach (IPEndPoint addr in addresses) {
                            Trace.WriteLine("DEBUG: am I source " + addr.Address.ToString());
                            if (who.Equals(addr.Address)) {
                                if (service.Type.StartsWith(Shared.DisplayCastGlobals.STREAMER))
                                    ami.streamer = service.Name;
                            }
                        }
                    }
                    responseString = serializer.Serialize(ami);
                    break;

                case "LISTPLAYERS":
                    responseString = serializer.Serialize(players);
                    break;

                case "LISTARCHIVERS":
                    responseString = serializer.Serialize(archivers);
                    break;

                case "LISTSTREAMERS":
                    responseString = serializer.Serialize(streamers);
                    break;

                case "LISTSESSIONS":
                    responseString = serializer.Serialize(sessions);
                    break;

                case "STATUS":
                    if ((request.Url.Query == null) || (request.Url.Query.Length < 2)) {
                        response.StatusCode = (int)HttpStatusCode.BadRequest;
                        response.StatusDescription = DisplayCastGlobals.CONTROL_USAGE_STATUS;
                        responseString = JSONError(DisplayCastGlobals.CONTROL_JSON_SYNTAX_ERROR);
                    } else {
                        String id = request.Url.Query.Substring(1);
                        JSONSrcSink res = null;

                        // Could be status of players/archivers/streamers
                        foreach (JSONSrcSink clnt in players)
                            if (clnt.id.Equals(id)) {
                                res = clnt;
                                break;
                            }
                        if (res == null)
                            foreach (JSONSrcSink clnt in archivers)
                                if (clnt.id.Equals(id)) {
                                    res = clnt;
                                    break;
                                }
                        if (res == null)
                            foreach (JSONSrcSink clnt in streamers)
                                if (clnt.id.Equals(id)) {
                                    res = clnt;
                                    break;
                                }
                        if (res == null) {
                            response.StatusCode = (int)HttpStatusCode.NotFound;
                            response.StatusDescription = "ID: " + id + " unknown";
                            responseString = JSONError(DisplayCastGlobals.CONTROL_JSON_UNKNOWN_ERROR);
                        } else
                            responseString = serializer.Serialize(res);
                    }
                    break;

                case "SESSIONSTATUS":
                    if ((request.Url.Query == null) || (request.Url.Query.Length < 2)) {
                        response.StatusCode = (int)HttpStatusCode.BadRequest;
                        response.StatusDescription = DisplayCastGlobals.CONTROL_USAGE_SESSIONSTATUS;
                        responseString = JSONError(DisplayCastGlobals.CONTROL_JSON_SYNTAX_ERROR);
                    } else {
                        String id = request.Url.Query.Substring(1);
                        JSONSession res = null;

                        foreach (JSONSession sess in sessions)
                            if (sess.id.Equals(id)) {
                                res = sess;
                                break;
                            }
                        if (res == null) {
                            response.StatusCode = (int)HttpStatusCode.NotFound;
                            response.StatusDescription = "ID: " + id + " unknown";
                            responseString = JSONError(DisplayCastGlobals.CONTROL_JSON_UNKNOWN_ERROR);
                        } else
                            responseString = serializer.Serialize(res);
                    }
                    break;

                case "SNAPSHOT":
                    if ((request.Url.Query == null) || (request.Url.Query.Length < 2)) {
                        response.StatusCode = (int)HttpStatusCode.BadRequest;
                        response.StatusDescription = DisplayCastGlobals.CONTROL_USAGE_SNAPSHOT;
                        responseString = JSONError(DisplayCastGlobals.CONTROL_JSON_SYNTAX_ERROR);
                    } else {
                        String id = request.Url.Query.Substring(1);
                        char[] delimiters = { '=', '&' };
                        String[] words = id.Split(delimiters);

                        if ((words.Length < 2) || (!words[0].ToUpper().Equals("ID"))) {
                            response.StatusCode = (int)HttpStatusCode.BadRequest;
                            response.StatusDescription = DisplayCastGlobals.CONTROL_USAGE_SNAPSHOT;
                            responseString = JSONError(DisplayCastGlobals.CONTROL_JSON_SYNTAX_ERROR);
                        } else {
                            NetService service = null;

                            foreach (NetService s in sourceServices)
                                if (s.Name.Equals(words[1])) {
                                    service = s;

                                    break;
                                } 
#if DEBUG
                            else
                                responseString = responseString + "Are we: " + s.Name + "\n";
#endif

                            if (service == null) {
                                response.StatusCode = (int)HttpStatusCode.NotFound;
                                response.StatusDescription = "ID: " + words[1] + " unknown";
                                responseString = JSONError(
#if DEBUG
                                    responseString + 
#endif
                                    DisplayCastGlobals.CONTROL_JSON_UNKNOWN_ERROR + words[1]);
                            } else {
                                String port = null;

                                if (service.TXTRecordData != null) {
                                    byte[] txt = service.TXTRecordData;
                                    IDictionary dict = NetService.DictionaryFromTXTRecordData(txt);

                                    if (dict != null) {
                                        foreach (DictionaryEntry kvp in dict) {
                                            String key = (String)kvp.Key;

                                            key = key.ToUpper();
                                            if (key.Equals("IMAGEPORT")) {
                                                byte[] value = (byte[])kvp.Value;

                                                try {
                                                    port = Encoding.UTF8.GetString(value);
                                                } catch {
                                                }
                                                break;
                                            }
                                        }
                                    }
                                }

                                if (port == null) {
                                    response.ContentType = "image/png";
                                    Int32 reqWidth = 800, reqHeight = 600;

                                    if ((words.Length >= 4) && (words[3].ToUpper().Equals("WIDTH"))) {
                                        reqWidth = Convert.ToInt32(words[4]);
                                        reqHeight = reqWidth * reqHeight / 800;
                                    }

                                    Bitmap bmp = new Bitmap(reqWidth, reqHeight, PixelFormat.Format24bppRgb);
                                    Graphics g = Graphics.FromImage(bmp);
                                    Font rectangleFont = new Font("Arial", 10, FontStyle.Bold);

                                    g.SmoothingMode = SmoothingMode.AntiAlias;
                                    g.Clear(Color.LightGray);

                                    g.DrawString("Desktop snapshot from " + service.Name + " should go here", rectangleFont, SystemBrushes.WindowText, new PointF(10, 40));
                                    bmp.Save(response.OutputStream, ImageFormat.Png);

                                    g.Dispose();
                                    bmp.Dispose();
                                } else {
                                    IList addresses = service.Addresses;
                                    int portNum = Convert.ToInt32(port);

                                    foreach (System.Net.IPEndPoint addr in addresses) {
                                        System.Net.Sockets.TcpClient clntSocket = new System.Net.Sockets.TcpClient();
                                        try {
                                            clntSocket.Connect(addr.Address, portNum);
                                            if (clntSocket.Connected) {
                                                clntSocket.Close();

                                                response.Redirect("http://" + addr.Address.ToString() + ":" + port + request.Url.Query);
                                                break;
                                            }
                                        } catch (SocketException) {
                                            //Ignore and try the next address
                                        }
                                    }
                                    if (response.StatusCode != (int)HttpStatusCode.Redirect) {
                                        response.ContentType = "image/png";

                                        Int32 reqWidth = 800, reqHeight = 600;

                                        if ((words.Length >= 4) && (words[2].ToUpper().Equals("WIDTH"))) {
                                            reqWidth = Convert.ToInt32(words[3]);
                                            reqHeight = reqWidth * reqHeight / 800;
                                            Trace.WriteLine("DEBUG: Changed image dimensions to: " + reqWidth + "x" + reqHeight);
                                        }

                                        Bitmap bmp = new Bitmap(reqWidth, reqHeight, PixelFormat.Format24bppRgb);
                                        Graphics g = Graphics.FromImage(bmp);
                                        Font rectangleFont = new Font("Arial", 10, FontStyle.Bold);

                                        g.SmoothingMode = SmoothingMode.AntiAlias;
                                        g.Clear(Color.LightGray);

                                        g.DrawString("Desktop " + service.Name + "at " + port + " not responding", rectangleFont, SystemBrushes.WindowText, new PointF(10, 40));
                                        bmp.Save(response.OutputStream, ImageFormat.Png);

                                        g.Dispose();
                                        bmp.Dispose();
                                    }
                                }

                                response.Close();
                                return;
                            }
                        }
                    }
                    break;

                case "CONNECT":
                    if ((request.Url.Query == null) || (request.Url.Query.Length < 2)) {
                        response.StatusCode = (int)HttpStatusCode.BadRequest;
                        response.StatusDescription = DisplayCastGlobals.CONTROL_USAGE_CONNECT;
                        responseString = JSONError(DisplayCastGlobals.CONTROL_JSON_SYNTAX_ERROR);
                    } else {
                        String q = request.Url.Query.Substring(1);
                        char[] delimiters = { '=', '&' };
                        String[] words = q.Split(delimiters);

                        if (((words.Length == 4) || (words.Length == 6)) && words[0].ToUpper().Equals("SOURCE") && words[2].ToUpper().Equals("SINK")) {
                            String srcId = words[1], sinkId = words[3];
                            NetService service = null;

                            foreach (NetService s in sinkServices)
                                if (s.Name.Equals(sinkId)) {
                                    service = s;
                                    break;
                                }

                            // We don't sanity check sources. Let the sink report an error, maybe the sink knows something that we don't know
                            if (service == null) {
                                response.StatusCode = (int)HttpStatusCode.NotFound;
                                response.StatusDescription = "Unknown sink: " + sinkId;
                                responseString = JSONError(DisplayCastGlobals.CONTROL_JSON_UNKNOWN_ERROR);
                            } else {
                                JSONnewSession ns = new JSONnewSession();
                                ns.id = sendCommand(service, "SHOW " + srcId + "\n");
                                responseString = serializer.Serialize(ns);
                            }
                        } else {
                            response.StatusDescription = DisplayCastGlobals.CONTROL_USAGE_CONNECT;
                            response.StatusCode = (int)HttpStatusCode.BadRequest;
                            responseString = JSONError(DisplayCastGlobals.CONTROL_JSON_SYNTAX_ERROR);
                        }
                    }
                    break;

                case "DISCONNECT":
                    if ((request.Url.Query == null) || (request.Url.Query.Length < 2)) {
                        response.StatusCode = (int)HttpStatusCode.BadRequest;
                        response.StatusDescription = DisplayCastGlobals.CONTROL_USAGE_DISCONNECT;
                        responseString = JSONError(DisplayCastGlobals.CONTROL_JSON_SYNTAX_ERROR);
                    } else {
                        String g = request.Url.Query.Substring(1);
                        char[] delimiters = { '=', '&' };
                        String[] words = g.Split(delimiters);
                        NetService service = null;
                        String sinkId = null;

                        if (((words.Length == 4) || (words.Length == 2)) & (words[0].ToUpper().Equals("ID"))) {
                            String debug = null;
                            foreach (JSONSession sess in sessions) {
                                debug = debug + " " + sess.id;
                                if (words[1].Equals(sess.id)) {
                                    sinkId = sess.sinkId;

                                    break;
                                }
                            }
                            if (sinkId == null) {
                                response.StatusDescription = "Unknown session ID: " + words[1];
                                response.StatusCode = (int)HttpStatusCode.NotFound;
                                responseString = JSONError("UNKNOWN ID: " + words[1] + " looked at " + debug);
                            } else {
                                foreach (NetService s in sinkServices)
                                    if (s.Name.Equals(sinkId)) {
                                        service = s;
                                        break;
                                    }

                                if (service == null) {
                                    response.StatusDescription = "Unknown sink ID: " + sinkId;
                                    response.StatusCode = (int)HttpStatusCode.NotFound;
                                    responseString = JSONError(DisplayCastGlobals.CONTROL_JSON_UNKNOWN_ERROR);
                                } else {
                                    JSONstatus status = new JSONstatus();
                                    status.result = sendCommand(service, "CLOSE " + words[1] + "\n");
                                    responseString = serializer.Serialize(status);
                                }
                            }
                        } else {
                            response.StatusDescription = DisplayCastGlobals.CONTROL_USAGE_DISCONNECT;
                            response.StatusCode = (int)HttpStatusCode.BadRequest;
                            responseString = JSONError(DisplayCastGlobals.CONTROL_JSON_SYNTAX_ERROR);
                        }

                    }
                    break;

                case "MOVE":
                    if ((request.Url.Query == null) || (request.Url.Query.Length < 2)) {
                        response.StatusCode = (int)HttpStatusCode.BadRequest;
                        response.StatusDescription = DisplayCastGlobals.CONTROL_USAGE_MOVE;
                        responseString = JSONError(DisplayCastGlobals.CONTROL_JSON_SYNTAX_ERROR);
                    } else {
                        String q = request.Url.Query.Substring(1);
                        char[] delimiters = { '=', '&' };
                        String[] words = q.Split(delimiters);

                        if (((words.Length == 10) || (words.Length == 12)) && words[0].ToUpper().Equals("SESSIONID") && words[2].ToUpper().Equals("X") && words[4].ToUpper().Equals("Y") && words[6].ToUpper().Equals("WIDTH") && words[8].ToUpper().Equals("HEIGHT")) {
                            NetService service = null;
                            String sinkId = null;
                            JSONSession session = null;
                            String sessId = words[1];

                            foreach (JSONSession sess in sessions)
                                if (sessId.Equals(sess.id)) {
                                    sinkId = sess.sinkId;
                                    session = sess;
                                    break;
                                }

                            if (sinkId == null) {
                                response.StatusCode = (int)HttpStatusCode.NotFound;
                                response.StatusDescription = "Unknown session: " + sessId;
                                responseString = JSONError(DisplayCastGlobals.CONTROL_JSON_UNKNOWN_ERROR);
                            } else {
                                foreach (NetService s in sinkServices)
                                    if (s.Name.Equals(sinkId)) {
                                        service = s;
                                        break;
                                    }

                                if (service == null) {
                                    response.StatusCode = (int)HttpStatusCode.NotFound;
                                    response.StatusDescription = "Unknown sink";
                                    responseString = JSONError(DisplayCastGlobals.CONTROL_JSON_UNKNOWN_ERROR);
                                } else {
                                    JSONstatus status = new JSONstatus();
                                    status.result = sendCommand(service, "MOVE " + sessId + " " + words[3] + "x" + words[5] + "x" + words[7] + "x" + words[9] + "\n");

                                    responseString = serializer.Serialize(status);
                                }
                            }
                        } else {
                            response.StatusDescription = DisplayCastGlobals.CONTROL_USAGE_MOVE;
                            response.StatusCode = (int)HttpStatusCode.BadRequest;
                            responseString = JSONError(DisplayCastGlobals.CONTROL_JSON_SYNTAX_ERROR);
                        }
                    }
                    break;

                case "ICONIFY":
                    if ((request.Url.Query == null) || (request.Url.Query.Length < 2)) {
                        response.StatusCode = (int)HttpStatusCode.BadRequest;
                        response.StatusDescription = DisplayCastGlobals.CONTROL_USAGE_ICON;
                        responseString = JSONError(DisplayCastGlobals.CONTROL_JSON_SYNTAX_ERROR);
                    } else {
                        String sessId = request.Url.Query.Substring(1);
                        NetService service = null;
                        String sinkId = null;
                        JSONSession session = null;

                        foreach (JSONSession sess in sessions)
                            if (sessId.Equals(sess.id)) {
                                sinkId = sess.id;
                                session = sess;
                                break;
                            }

                        if (sinkId == null) {
                            response.StatusCode = (int)HttpStatusCode.NotFound;
                            response.StatusDescription = "Unknown session: " + sessId;
                            responseString = JSONError(DisplayCastGlobals.CONTROL_JSON_UNKNOWN_ERROR);
                        } else {
                            foreach (NetService s in sinkServices)
                                if (s.Name.Equals(sinkId)) {
                                    service = s;
                                    break;
                                }

                            if (service == null) {
                                response.StatusCode = (int)HttpStatusCode.NotFound;
                                response.StatusDescription = "Unknown sink";
                                responseString = JSONError(DisplayCastGlobals.CONTROL_JSON_UNKNOWN_ERROR);
                            } else {
                                JSONstatus status = new JSONstatus();
                                status.result = sendCommand(service, ((session.iconified == 0) ? "ICON " : "DICO ") + sessId + "\n");
                                responseString = serializer.Serialize(status);
                            }

                        }
                    }
                    break;

                case "FULLSCREEN":
                    if ((request.Url.Query == null) || (request.Url.Query.Length < 2)) {
                        response.StatusCode = (int)HttpStatusCode.BadRequest;
                        response.StatusDescription = DisplayCastGlobals.CONTROL_USAGE_FULLSCREEN;
                        responseString = JSONError(DisplayCastGlobals.CONTROL_JSON_SYNTAX_ERROR);
                    } else {
                        String sessId = request.Url.Query.Substring(1);
                        NetService service = null;
                        String sinkId = null;
                        JSONSession session = null;

                        foreach (JSONSession sess in sessions)
                            if (sessId.Equals(sess.id)) {
                                sinkId = sess.id;
                                session = sess;
                                break;
                            }

                        if (sinkId == null) {
                            response.StatusCode = (int)HttpStatusCode.NotFound;
                            response.StatusDescription = "Unknown id: " + sinkId;
                            responseString = JSONError(DisplayCastGlobals.CONTROL_JSON_UNKNOWN_ERROR);
                        } else {
                            foreach (NetService s in sinkServices)
                                if (s.Name.Equals(sinkId)) {
                                    service = s;
                                    break;
                                }

                            if (service == null) {
                                response.StatusCode = (int)HttpStatusCode.NotFound;
                                response.StatusDescription = "Unknown sink";
                                responseString = JSONError(DisplayCastGlobals.CONTROL_JSON_UNKNOWN_ERROR);
                            } else {
                                JSONstatus status = new JSONstatus();
                                status.result = sendCommand(service, ((session.fullScreen == 0) ? "FS " : "SF ") + sessId + "\n");
                                responseString = serializer.Serialize(status);
                            }
                        }
                    }
                    break;

                case "MASK":
                case "CREATEREGION":
                    if ((request.Url.Query == null) || (request.Url.Query.Length < 2)) {
                        response.StatusCode = (int)HttpStatusCode.BadRequest;
                        response.StatusDescription = DisplayCastGlobals.CONTROL_USAGE_MASK;
                        responseString = JSONError(DisplayCastGlobals.CONTROL_JSON_SYNTAX_ERROR);
                    } else {
                        String q = request.Url.Query.Substring(1);
                        char[] delimiters = { '=', '&' };
                        String[] words = q.Split(delimiters);

                        if (((words.Length == 10) || (words.Length == 12)) && words[0].ToUpper().Equals("SOURCE") && words[2].ToUpper().Equals("X") && words[4].ToUpper().Equals("Y") && words[6].ToUpper().Equals("WIDTH") && words[8].ToUpper().Equals("HEIGHT")) {
                            NetService service = null;
                            String srcId = words[1];

                            foreach (NetService s in sourceServices)
                                if (srcId.Equals(s.Name)) {
                                    service = s;
                                    break;
                                }

                            if (service == null) {
                                response.StatusCode = (int)HttpStatusCode.NotFound;
                                response.StatusDescription = "Unknown source: " + srcId;
                                responseString = JSONError(DisplayCastGlobals.CONTROL_JSON_UNKNOWN_ERROR);
                            } else {
                                Int32 port = -1;

                                if (service.TXTRecordData != null) {
                                    byte[] txt = service.TXTRecordData;
                                    IDictionary dict = NetService.DictionaryFromTXTRecordData(txt);

                                    if (dict != null) {
                                        foreach (DictionaryEntry kvp in dict) {
                                            String key = (String)kvp.Key;

                                            key = key.ToUpper();
                                            if (key.Equals("MASKPORT")) {
                                                byte[] value = (byte[])kvp.Value;

                                                try {
                                                    port = Convert.ToInt32(Encoding.UTF8.GetString(value));
                                                } catch {
                                                }
                                                break;
                                            }
                                        }
                                    }
                                }

                                if (port > 0) {
                                    JSONstatus status = new JSONstatus();
                                    status.result = sendCommand(service, port, "MASK " + words[3] + " " + words[5] + " " + words[7] + " " + words[9] + "\n");

                                    responseString = serializer.Serialize(status);
                                } else {
                                    response.StatusDescription = "Streamer does not support masking";
                                    response.StatusCode = (int)HttpStatusCode.NotImplemented;
                                    responseString = JSONError(DisplayCastGlobals.CONTROL_JSON_UNIMPL_ERROR);
                                }
                            }
                        } else {
                            response.StatusDescription = DisplayCastGlobals.CONTROL_USAGE_MASK;
                            response.StatusCode = (int)HttpStatusCode.BadRequest;
                            responseString = JSONError(DisplayCastGlobals.CONTROL_JSON_SYNTAX_ERROR);
                        }
                    }
                    break;

                default:
                    response.StatusCode = (int)HttpStatusCode.BadRequest;
                    response.StatusDescription = "Unknown command: " + request.RawUrl;
                    responseString = JSONError(DisplayCastGlobals.CONTROL_JSON_UNKNOWN_ERROR + request.RawUrl + " " + cmd);
                    break;
            }

            // Now wrap in JSONP response
            if (response.StatusCode == (int)HttpStatusCode.OK) {
                var sb = new System.Text.StringBuilder();
                var queryString = System.Web.HttpUtility.ParseQueryString(request.Url.Query);
                var callback = queryString["callback"] ?? "callback";
                sb.Append(callback + "(" + responseString + ");");
                responseString = sb.ToString();
            }

            // Send the response back
            byte[] buffer = System.Text.Encoding.UTF8.GetBytes(responseString);
            response.ContentLength64 = buffer.Length;
            response.Headers.Set(HttpResponseHeader.Server, "FXPAL DisplayCast/" + DisplayCastGlobals.DISPLAYCAST_VERSION + " and not");
            response.OutputStream.Write(buffer, 0, buffer.Length);
            response.Close();
        }

        /// <summary>
        /// Class constructor
        /// </summary>
        /// <param name="sessions">Array to hold sessions</param>
        /// <param name="players">Array to hold players</param>
        /// <param name="streamers">Array to hold streamers</param>
        /// <param name="archivers">Array to hold all archiers</param>
        /// <param name="sinks">NetService array of all sinks</param>
        /// <param name="sources">NetService array of all sources</param>
        public APIresponder(ArrayList sessions, ArrayList players, ArrayList streamers, ArrayList archivers, ArrayList sinks, ArrayList sources) {
            this.sessions = sessions;
            this.players = players;
            this.streamers = streamers;
            this.archivers = archivers;
            this.sinkServices = sinks;
            this.sourceServices = sources;

            listener.Prefixes.Add( Shared.DisplayCastGlobals.CONTROL_API_URL);
            listener.Start();

            IAsyncResult result = this.listener.BeginGetContext(new AsyncCallback(WebRequestCallback), this.listener);
        }
    }
}