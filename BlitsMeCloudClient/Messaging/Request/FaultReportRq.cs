﻿using System;
using System.Runtime.Serialization;

namespace BlitsMe.Cloud.Messaging.Request
{
    [DataContract]
    public class FaultReportRq : API.Request
    {
        public override String type
        {
            get { return "FaultReport-RQ"; }
            set { }
        }
        [DataMember]
        public string log;
        [DataMember]
        public String report;
    }
}