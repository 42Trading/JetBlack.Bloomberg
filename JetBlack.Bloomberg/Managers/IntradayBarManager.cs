﻿using System;
using System.Collections.Generic;
using Bloomberglp.Blpapi;
using JetBlack.Bloomberg.Exceptions;
using JetBlack.Bloomberg.Identifiers;
using JetBlack.Bloomberg.Models;
using JetBlack.Bloomberg.Patterns;
using JetBlack.Bloomberg.Requests;
using JetBlack.Bloomberg.Utilities;
using JetBlack.Monads;

namespace JetBlack.Bloomberg.Managers
{
    public class IntradayBarManager
    {
        private readonly IDictionary<CorrelationID, AsyncPattern<TickerIntradayBarData>> _asyncHandlers = new Dictionary<CorrelationID, AsyncPattern<TickerIntradayBarData>>();
        private readonly IDictionary<CorrelationID, string> _tickerMap = new Dictionary<CorrelationID, string>();
        private readonly IDictionary<CorrelationID, TickerIntradayBarData> _partial = new Dictionary<CorrelationID, TickerIntradayBarData>();

        public IPromise<TickerIntradayBarData> Request(Session session, Identity identity, Service refDataService, IntradayBarRequest request)
        {
            return new Promise<TickerIntradayBarData>((resolve, reject) =>
            {
                var correlationId = new CorrelationID();
                _asyncHandlers.Add(correlationId, AsyncPattern<TickerIntradayBarData>.Create(resolve, reject));
                _tickerMap.Add(correlationId, request.Ticker);
                session.SendRequest(request.Create(refDataService), identity, correlationId);
            });
        }

        public void Process(Session session, Message message, bool isPartialResponse, Action<Session, Message, Exception> onFailure)
        {
            AsyncPattern<TickerIntradayBarData> asyncHandler;
            if (!_asyncHandlers.TryGetValue(message.CorrelationID, out asyncHandler))
            {
                onFailure(session, message, new Exception("Unable to find handler for correlation id: " + message.CorrelationID));
                return;
            }

            var ticker = _tickerMap[message.CorrelationID];
            _tickerMap.Remove(message.CorrelationID);

            if (message.HasElement(ElementNames.ResponseError))
            {
                asyncHandler.OnFailure(new ContentException<TickerResponseError>(new TickerResponseError(ticker, message.GetElement(ElementNames.ResponseError).ToResponseError())));
                return;
            }

            var barData = message.GetElement(ElementNames.BarData);

            TickerIntradayBarData tickerIntradayBarData;
            if (_partial.TryGetValue(message.CorrelationID, out tickerIntradayBarData))
                _partial.Remove(message.CorrelationID);
            else
            {
                var entitlementIds = barData.HasElement(ElementNames.EidData) ? barData.GetElement(ElementNames.EidData).ExtractEids() : null;
                tickerIntradayBarData = new TickerIntradayBarData(ticker, new List<IntradayBar>(), entitlementIds);
            }

            var barTickData = barData.GetElement(ElementNames.BarTickData);

            for (var i = 0; i < barTickData.NumValues; ++i)
            {
                var element = barTickData.GetValueAsElement(i);
                tickerIntradayBarData.IntradayBars.Add(
                    new IntradayBar(
                        element.GetElementAsDatetime(ElementNames.Time).ToDateTime(),
                        element.GetElementAsFloat64(ElementNames.Open),
                        element.GetElementAsFloat64(ElementNames.High),
                        element.GetElementAsFloat64(ElementNames.Low),
                        element.GetElementAsFloat64(ElementNames.Close),
                        element.GetElementAsInt32(ElementNames.NumEvents),
                        element.GetElementAsInt64(ElementNames.Volume)));
            }

            if (isPartialResponse)
            {
                _tickerMap.Add(message.CorrelationID, ticker);
                _partial[message.CorrelationID] = tickerIntradayBarData;
            }
            else
                asyncHandler.OnSuccess(tickerIntradayBarData);
        }
    }
}
