// Copyright (c) 2012, Fuji Xerox Co., Ltd.
// All rights reserved.
// Author: Surendar Chandra, FX Palo Alto Laboratory, Inc.

using System;
using System.Collections.Generic;
using System.Text;

namespace location {
    public class XmlNoNamespaceWriter : System.Xml.XmlTextWriter {
        bool skipAttribute = false;

        public XmlNoNamespaceWriter(System.IO.TextWriter writer)
            : base(writer) {
        }

        public override void WriteStartElement(string prefix, string localName, string ns) {
            base.WriteStartElement(String.Empty, localName, "http://cisco.com/mse/location");
        }


        public override void WriteStartAttribute(string prefix, string localName, string ns) {
            //If the prefix or localname are "xmlns", don't write it.
            if (prefix.CompareTo("xmlns") == 0 || localName.CompareTo("xmlns") == 0) {
                skipAttribute = true;
            } else {
                base.WriteStartAttribute(String.Empty, localName, "http://cisco.com/mse/location");
            }
        }

        public override void WriteString(string text) {
            //If we are writing an attribute, the text for the xmlns
            //or xmlns:prefix declaration would occur here.  Skip
            //it if this is the case.
            if (!skipAttribute) {
                base.WriteString(text);
            }
        }

        public override void WriteEndAttribute() {
            //If we skipped the WriteStartAttribute call, we have to
            //skip the WriteEndAttribute call as well or else the XmlWriter
            //will have an invalid state.
            if (!skipAttribute) {
                base.WriteEndAttribute();
            }
            //reset the boolean for the next attribute.
            skipAttribute = false;
        }


        public override void WriteQualifiedName(string localName, string ns) {
            //Always write the qualified name using only the
            //localname.
            base.WriteQualifiedName(localName, "http://cisco.com/mse/location");
        }
    }
}