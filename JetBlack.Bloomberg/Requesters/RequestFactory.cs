﻿using System.Collections.Generic;
using Bloomberglp.Blpapi;

namespace JetBlack.Bloomberg.Requesters
{
    public abstract class RequestFactory
    {
        public ICollection<string> Tickers { get; set; }
        public abstract IEnumerable<Request> CreateRequests(Service refDataService);
    }
}