# Zeta
No-nonsense, high performance pub-sub for distributing time-critical data over UDP.

## TLDR - How to I make it go?
Create a server and publish a stream of realtime data:
```c#
var server = new ZetaServer<StringMessage>(1000); // Listed on UDP port 1000, and be prepared to send StringMessages (could be BinaryMessage or something custom)
server.Publish(new StringMessage("Message 1"));
server.Publish(new StringMessage("Message 2"));
server.Publish(new StringMessage("Message 3"));
server.Publish(new StringMessage("Message 4"));
// etc...
```

Create clients and subscribe to the data stream:
```c$
var client = new ZetaClient<StringMessage>("127.0.0.1:1000", (topic, revision, payload) => { // Connect to localhost:1000
    Console.WriteLine($"Received value of '{payload.Value}'."); // Called every time an update arrives
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