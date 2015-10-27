﻿using System;
using System.Collections.Generic;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using Bloomberglp.Blpapi;
using JetBlack.Bloomberg.Exceptions;
using JetBlack.Bloomberg.Identifiers;
using JetBlack.Bloomberg.Models;
using JetBlack.Bloomberg.Requests;
using JetBlack.Bloomberg.Utilities;

namespace JetBlack.Bloomberg.Managers
{
    internal class IntradayTickManager : RequestResponseManager<IntradayTickRequest, IntradayTickResponse>, IIntradayTickProvider
    {
        private readonly Service _service;
        private readonly Identity _identity; 
        
        private readonly IDictionary<CorrelationID, string> _tickerMap = new Dictionary<CorrelationID, string>(); 

        public IntradayTickManager(Session session, Service service, Identity identity)
            : base(session)
        {
            _service = service;
            _identity = identity;
        }

        public override IObservable<IntradayTickResponse> ToObservable(IntradayTickRequest request)
        {
            return Observable.Create<IntradayTickResponse>(observer =>
            {
                var correlationId = new CorrelationID();
                Observers.Add(correlationId, observer);
                _tickerMap.Add(correlationId, request.Ticker);
                Session.SendRequest(request.ToRequest(_service), _identity, correlationId);

                return Disposable.Create(() => Session.Cancel(correlationId));
            });
        }

        public override bool CanProcessResponse(Message message)
        {
            return MessageTypeNames.IntradayTickResponse.Equals(message.MessageType);
        }

        public override void ProcessResponse(Session session, Message message, bool isPartialResponse, Action<Session, Message, Exception> onFailure)
        {
            IObserver<IntradayTickResponse> observer;
            if (!Observers.TryGetValue(message.CorrelationID, out observer))
            {
                onFailure(session, message, new Exception("Unable to find handler for correlation id: " + message.CorrelationID));
                return;
            }

            var ticker = _tickerMap[message.CorrelationID];
            _tickerMap.Remove(message.CorrelationID);

            if (message.HasElement(ElementNames.ResponseError))
            {
                observer.OnError(new ContentException<TickerResponseError>(new TickerResponseError(ticker, message.GetElement(ElementNames.ResponseError).ToResponseError())));
                return;
            }

            var tickData = message.GetElement("tickData").GetElement("tickData");

            var entitlementIds = tickData.HasElement("eidData") ? tickData.GetElement("eidData").ExtractEids() : null;
            var intradayTickResponse = new IntradayTickResponse(ticker, new List<IntradayTick>(), entitlementIds);

            for (var i = 0; i < tickData.NumValues; ++i)
            {
                var item = tickData.GetValueAsElement(i);
                var conditionCodes = (item.HasElement("conditionCodes") ? item.GetElementAsString("conditionCodes").Split(',') : null);
                var exchangeCodes = (item.HasElement("exchangeCode") ? item.GetElementAsString("exchangeCode").Split(',') : null);

                intradayTickResponse.IntraDayTicks.Add(
                    new IntradayTick(
                        item.GetElementAsDatetime("time").ToDateTime(),
                        (EventType)Enum.Parse(typeof(EventType), item.GetElementAsString("type"), true),
                        item.GetElementAsFloat64("value"),
                        item.GetElementAsInt32("size"),
                        conditionCodes,
                        exchangeCodes));
            }

            observer.OnNext(intradayTickResponse);

            if (!isPartialResponse)
            {
                observer.OnCompleted();
                Observers.Remove(message.CorrelationID);
            }
        }
    }
}
