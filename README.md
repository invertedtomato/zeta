# Zeta
No-nonsense, high performance pub-sub for distributing time-critical data over UDP.

## How to I make it go?
Create a server and publish a stream of realtime data:
```c#
var server = new ZetaUdpPublisher(); 
server.Start(1000); // Listed on UDP port 1000
server.Publish(new StringMessage("Message 1")); // Publish a stream of messages
server.Publish(new StringMessage("Message 2"));
server.Publish(new StringMessage("Message 3"));
// etc...
```

Create clients and subscribe to the data stream:
```c$
var client = new ZetaUdpSubscriber();
client.Start("127.0.0.1:1000"); // Connect to localhost port 1000
while (true) {
    var msg = client.Read<StringMessage>(); // Receive message
    Console.WriteLine($"> {msg}");
}
```

If values are published faster than clients can recieve them, the older "stale" updates will be dropped in favour of the more recent updates.

If you have multiple seperate streams of data, you can seperate them by assigning a numeric "topic" to each stream:
```c#
server.Publish(new StringMessage("Topic 1, message 1"), 1);
server.Publish(new StringMessage("Topic 2, message 1"), 2);
```
This means that if receivers incur any form of delay receiving updates, they will always receive the latest update for each topic.

## What happens if a client connects after a value is published?
The server will automatically retransmit the latest value for each topic.

You can stop this from occuring by publishing a NULL on a topic:
```c#
server.Publish(null, 1);
```
## Whats the deal with StringMessage? Are there other message types?
Of course! StringMessage is the simplest type of message for transporting strings. You can use BinaryMessage for byte arrays or create your own for even better control. See [InvertedTomato.Messages](https://github.com/invertedtomato/messages) for all types. Feel free to request (or pull-request) new ones - they're super easy to make.

## Will updates arrive in order? (UDP doesn't garantee order)
You'll never receive an older update after a newer one. It is possible that an update may be lost though in favour of a newer one, as discussed above.

## Does Zeta retransmit updates if they're lost (UDP doesn't garantee reliability)
Yes, Zeta will retransmit updates as needed.

## What is the maximum size of a message?
It depends on your network. Most networks support UDP packets of 1,500 bytes or greater. However old-school networks might only support 520 bytes. By default Zeta is configured to use this worst-case value of 520 bytes. Change this in `Mtu` if needed.

If a message exceeds the maximum of the network (and you've incorrectly increased the maximum in `Mtu`) the messages will never arrive at clients.

## Is it thread safe?
Zeta is thread safe. You can call `Publish` from as many threads as you want, simaltaniously. 