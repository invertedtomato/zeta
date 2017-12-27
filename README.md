# Zeta
No-nonsense, high performance pub-sub for distributing time-critical data over UDP.

## TLDR - How to I make it go?
Define a message you'd like to send to clients:
```c#
[ProtoContract] // Refer to https://github.com/mgravell/protobuf-net for attribute details
public class Message {
    public Message() { }
    public Message(int value) { Value = value; }

    [ProtoMember(1)]
    public int Value { get; set; }
}
```

Create a server and publish a stream of realtime data:
```c#
var server = new ZetaServer<Message>(1000);  // 1000 is the port to listen on
server.Publish(new Message(0));
server.Publish(new Message(1));
server.Publish(new Message(2));
// etc...
```

Create clients and subscribe to the data stream:
```c$
var serverEndPoint = new IPEndPoint(new IPAddress(new byte[]{ 127, 0, 0, 1 }), 1000);
var client = new ZetaClient<Message>(serverEndPoint, (topic, revision, value) => {
    Console.WriteLine($"{topic}#{revision}={value.Value}"); // Called every time an update arrives
});
```

## Overview
The above example uses ProtoBuf under the hood to seralize your objects into a byte array. It's not nesscessarily the most efficent. In reality you get more control to use it like this:
Create a server and publish a stream of realtime data:
```c#
var server = new ZetaServer(1000);
server.Publish(1, new byte[] { 0 }); // '1' is the topic to send on
server.Publish(1, new byte[] { 1 });
server.Publish(1, new byte[] { 2 });
// etc...
```

Create clients and subscribe to the data stream:
```c$
var serverEndPoint = new IPEndPoint(new IPAddress(new byte[]{ 127, 0, 0, 1 }), 1000);
var client = new ZetaClient(serverEndPoint, (topic, revision, value) => {
    Console.WriteLine($"{topic}#{revision}={value[0]}"); // Value is a byte array
});
```

## Notes
### Maximum transmission unit
In order to achieve it's high performance, Zeta uses UDP sockets without fragmentation. This means that there is a maximum message size. Most networks support 1,500 byte, however others may be less - as little as 520 bytes. By default Zeta is configured for the worst-case value of 520. If you know your MTU, set the value in Options.Mtu.

### Retransmission and out-of-order
If you know much about UDP you'll know that it's "unreliable" - packets might arrive in the wrong order, or not at all. Zeta manages this and ensures that messages arrive in the same order they were transmitted, and will be retransmitted if lost.

### Threading
Zeta is threadsafe. 

### Topics
In the above example we have assumed you have one stream of data you want to send. However in most real-world cases you'll have more. You can mark your streams like this:
```c#
server.Publish(<topic>, new Message(0));
```