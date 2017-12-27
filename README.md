# Zeta
No-nonsense, high performance pub-sub for distributing time-critical data over UDP.

## TLDR
Create a message you'd like to send to clients
```c#
[ProtoContract] // Refer to https://github.com/mgravell/protobuf-net for attribute details
public class Message {
    public Message() { }
    public Message(int value) { Value = value; }

    [ProtoMember(1)]
    public int Value { get; set; }
}
```

Create a server and publish a stream of realtime data
```c#
var server = new ZetaServer<A>(1000);  // 1000 is the port to listen on
server.Publish(new Message(0));
server.Publish(new Message(1));
server.Publish(new Message(2));
// etc...
```

Create clients and subscribe to the data stream
```c$
var serverEndPoint = new IPEndPoint(new IPAddress(new byte[]{ 127, 0, 0, 1 }), 1000);
var client = new ZetaClient(serverEndPoint, (topic, revision, value) => {
    Console.WriteLine($"{topic}#{revision}={value[0]}"); // Called every time an update arrives
});
```
