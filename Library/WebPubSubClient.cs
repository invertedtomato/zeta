using System;
using System.Collections.Generic;
using System.Text;

namespace InvertedTomato.WebPubSub {
    public class WebPubSubClient : IDisposable {
        public Boolean IsDisposed { get; private set; }

        public WebPubSubClient(String endPoint, UInt64[] groups) {
            throw new NotImplementedException();
        }
        public void Subscribe<TMessage>(Action<UInt64, UInt16, TMessage> handler, UInt64 topicLow = 0, UInt64 topicHigh = 0) {
            throw new NotImplementedException();
        }


        protected virtual void Dispose(Boolean disposing) {
            if(IsDisposed) {
                return;
            }
            IsDisposed = true;

            if(disposing) {
                // TODO: dispose managed state (managed objects).
            }

            // TODO: free unmanaged resources (unmanaged objects) and override a finalizer below.
            // TODO: set large fields to null.
        }

        public void Dispose() {
            Dispose(true);
        }
    }
}
