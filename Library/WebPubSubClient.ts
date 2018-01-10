
export class WebPubSubClient {
    private readonly SUBPROTOCOL = 'webpubsub';
    private readonly KEEPALIVE_INTERVAL = 10 * 1000;
    private readonly keepAlive: number;
    private readonly socket: WebSocket;
    private readonly handlerRecords: Array<HandlerRecord> = [];

    public isDisposed: boolean = false;
    
    constructor(endpoint: string) {
        // Setup socket
        this.socket = new WebSocket(endpoint, this.SUBPROTOCOL);
        this.socket.binaryType = 'arraybuffer';
        this.socket.onopen = (e): void => { };
        this.socket.onclose = (e): void => { this.dispose(); };
        this.socket.onmessage = (message: MessageEvent): void => { this.onMessage(message); };

        // Setup keepalive
        this.keepAlive = setInterval((): void => {
            this.socket.send("");
        }, this.KEEPALIVE_INTERVAL)
    }

    private onMessage(a: MessageEvent): void {
        let buffer = new DataView(a.data as ArrayBuffer);

        // Parse
        var topic = buffer.getUint32(0, true);
        var revision = buffer.getUint32(4, true);

        // Get handler
        let selectedHandlerRecord: HandlerRecord;
        for (let x in this.handlerRecords) {
            let handlerRecord = this.handlerRecords[x];

            if (handlerRecord.topicLow <= topic && handlerRecord.topicHigh >= topic) {
                selectedHandlerRecord = handlerRecord;
                break;
            }
        }
        if (null == selectedHandlerRecord) {
            //Trace.TraceWarning($"RX: Unexpected topic {topic}#{revision}");
            return;
        }

        // Create message
        var message = new selectedHandlerRecord.messageType as IMessage;
        message.import(new DataView(a.data as ArrayBuffer, 8));

        // Raise handler
        selectedHandlerRecord.handler(topic, revision, message);
    }

    public subscribe<TMessage extends IMessage>(messageType: TMessage, handler: Handler<TMessage>, topicLow?: number, topicHigh?: number): void {
        if (this.isDisposed) {
            throw new Error("Cannot be used while disposed.");
        }

        // Handle overloads
        topicLow = topicLow || 0;
        topicHigh = topicHigh || topicLow;

        // Check request sanity
        if (topicHigh < topicLow) {
            throw new Error("topicHigh less than topicLow");
        }

        // Check for existing conflicting handler
        for (var x in this.handlerRecords) {
            let handlerRecord = this.handlerRecords[x];

            if ((handlerRecord.topicLow >= topicLow && handlerRecord.topicLow <= topicHigh) ||
                (handlerRecord.topicHigh <= topicLow && handlerRecord.topicHigh >= topicHigh)) {
                throw new Error("There is already a handler covering this range, or part of this range of topics.");
            }
        }
        
        // Add handler
        this.handlerRecords.push({
            topicHigh,
            topicLow,
            handler,
            messageType
        });
    }

    public dispose(): void {
        if (this.isDisposed) {
            return;
        }
        this.isDisposed = true;

        clearTimeout(this.keepAlive);
        this.socket.close();
    }
}

export interface Handler<TMessage> {
    (topic: number, revision: number, message: TMessage): void;
}
export interface IMessage {
    import(buffer: DataView): void;
    export(): DataView;
}

interface HandlerRecord {
    topicLow: number;
    topicHigh: number;
    handler: Handler<any>;
    messageType: any;
}