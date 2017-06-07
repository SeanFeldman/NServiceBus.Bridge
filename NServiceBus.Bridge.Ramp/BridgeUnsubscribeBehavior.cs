﻿using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NServiceBus;
using NServiceBus.Logging;
using NServiceBus.Pipeline;
using NServiceBus.Routing;
using NServiceBus.Transport;
using NServiceBus.Unicast.Transport;

class BridgeUnsubscribeBehavior : Behavior<IUnsubscribeContext>
{
    public BridgeUnsubscribeBehavior(string subscriberAddress, string subscriberEndpoint, string bridgeAddress, IDispatchMessages dispatcher, Dictionary<Type, string> publisherTable)
    {
        this.subscriberAddress = subscriberAddress;
        this.subscriberEndpoint = subscriberEndpoint;
        this.bridgeAddress = bridgeAddress;
        this.dispatcher = dispatcher;
        this.publisherTable = publisherTable;
    }

    public override Task Invoke(IUnsubscribeContext context, Func<Task> next)
    {
        var eventType = context.EventType;
        string publisherEndpoint;
        if (!publisherTable.TryGetValue(eventType, out publisherEndpoint))
        {
            return next(); //Invoke the terminator
        }

        Logger.Debug($"Sending unsubscribe request for {eventType.AssemblyQualifiedName} to bridge queue {bridgeAddress} to be forwarded to {publisherEndpoint}");

        var subscriptionMessage = ControlMessageFactory.Create(MessageIntentEnum.Unsubscribe);

        subscriptionMessage.Headers[Headers.SubscriptionMessageType] = eventType.AssemblyQualifiedName;
        subscriptionMessage.Headers[Headers.ReplyToAddress] = subscriberAddress;
        subscriptionMessage.Headers[Headers.SubscriberTransportAddress] = subscriberAddress;
        subscriptionMessage.Headers[Headers.SubscriberEndpoint] = subscriberEndpoint;
        subscriptionMessage.Headers["NServiceBus.Bridge.DestinationEndpoint"] = publisherEndpoint;
        subscriptionMessage.Headers[Headers.TimeSent] = DateTimeExtensions.ToWireFormattedString(DateTime.UtcNow);
        subscriptionMessage.Headers[Headers.NServiceBusVersion] = "6.3.1"; //The code has been copied from 6.3.1

        var transportOperation = new TransportOperation(subscriptionMessage, new UnicastAddressTag(bridgeAddress));
        var transportTransaction = context.Extensions.GetOrCreate<TransportTransaction>();
        return dispatcher.Dispatch(new TransportOperations(transportOperation), transportTransaction, context.Extensions);
    }

    IDispatchMessages dispatcher;
    Dictionary<Type, string> publisherTable;
    string subscriberAddress;
    string subscriberEndpoint;
    string bridgeAddress;

    static ILog Logger = LogManager.GetLogger<BridgeUnsubscribeBehavior>();
}
