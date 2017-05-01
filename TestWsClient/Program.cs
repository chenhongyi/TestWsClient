using ProtoBuf;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace TestWsClient
{
    public class ProtobufWsSerializer : IWsSerializer
    {
        public Task<byte[]> SerializeAsync<T>(T value)
        {
            using (MemoryStream ms = new MemoryStream())
            {
                Serializer.Serialize(ms, value);
                return Task.FromResult(ms.ToArray());
            }
        }

        public Task<T> DeserializeAsync<T>(byte[] serialized)
        {
            return this.DeserializeAsync<T>(serialized, 0, serialized.Length);
        }

        public Task<T> DeserializeAsync<T>(byte[] serialized, int offset, int length)
        {
            using (MemoryStream ms = new MemoryStream(serialized, offset, length))
            {
                return Task.FromResult(Serializer.Deserialize<T>(ms));
            }
        }
    }
    public static class SerializerFactory
    {
        // Set JsonNet as the default serializer
        private static Func<IWsSerializer> DefaultFactory = () => new ProtobufWsSerializer();

        public static void SetFactory<T>() where T : IWsSerializer, new()
        {
            DefaultFactory = () => new T();
        }

        public static IWsSerializer CreateSerializer()
        {
            return DefaultFactory();
        }
    }
    public interface IWsSerializer
    {
        Task<byte[]> SerializeAsync<T>(T value);
        Task<T> DeserializeAsync<T>(byte[] serialized);
        Task<T> DeserializeAsync<T>(byte[] serialized, int offset, int length);
    }
    class Program
    {
        private WebSocketManager websocketManager = null;
        static void Main(string[] args)
        {
            PostProductModel postProductModel = new PostProductModel
            {
                // generating new user id for every iteration
                ProductId = 701,
                Quantity = 100
            };

            string url = "ws://127.0.0.1:3251/PublicGatewayWS";
            IWsSerializer serializer = SerializerFactory.CreateSerializer();
            byte[] payload = serializer.SerializeAsync(postProductModel).Result;

            WsRequestMessage mreq = new WsRequestMessage
            {
                PartitionKey = 701,
                Operation = "additem",
                Value = payload
            };


            using (PublicGatewayWebSocketClient websocketClient = new PublicGatewayWebSocketClient())
            {
                var b = websocketClient.ConnectAsync(url).Result;
                for (int i = 0; i < 3; i++)
                {
                    WsResponseMessage mresp = websocketClient.SendReceiveAsync(mreq, CancellationToken.None).Result;
                    PostProductModel result = serializer.DeserializeAsync<PostProductModel>(mresp.Value).Result;
                }
                
            }

            Console.ReadKey();
        }
    }


    [ProtoContract]
    public class PostProductModel
    {
        [ProtoMember(1)]
        public int ProductId { get; set; }

        [ProtoMember(2)]
        public int Quantity { get; set; }
    }


    public class PublicGatewayWebSocketClient : IDisposable
    {
        private WebSocketManager websocketManager = null;

        public PublicGatewayWebSocketClient()
        {
        }

        public void Dispose()
        {
            try
            {
                this.CloseAsync().Wait();
            }
            catch (ObjectDisposedException)
            {
            }
            catch (AggregateException ae)
            {
                ae.Handle(
                    ex =>
                    {
                        return true;
                    });
            }
            finally
            {
                this.websocketManager = null;
            }
        }

        public async Task<bool> ConnectAsync(string serviceName)
        {
            //Uri serviceAddress = new Uri(new Uri(serviceName), ServiceConst.DataApiWebsockets);
            if (this.websocketManager == null)
            {
                this.websocketManager = new WebSocketManager();
            }
            Uri serviceAddress = new Uri("ws://127.0.0.1:3251/PublicGatewayWS");
            return await this.websocketManager.ConnectAsync(serviceAddress);
        }

         public async Task CloseAsync()
        {
            try
            {
                if (this.websocketManager != null)
                {
                    await this.websocketManager.CloseAsync();
                    this.websocketManager.Dispose();
                }
            }
            catch (Exception ex)
            {
            }
            finally
            {
                this.websocketManager = null;
            }
        }

        /// <summary>
        /// Re-uses the open websocket connection (assumes one is already created/connected)
        /// </summary>
        public async Task<WsResponseMessage> SendReceiveAsync(WsRequestMessage msgspec, CancellationToken cancellationToken)
        {
            if (this.websocketManager == null)
            {
                throw new ApplicationException("SendReceiveAsync requires an open websocket client");
            }

            // Serialize Msg payload
            IWsSerializer mserializer = new ProtobufWsSerializer();

            byte[] request = await mserializer.SerializeAsync(msgspec);

            byte[] response = await this.websocketManager.SendReceiveAsync(request);

            return await mserializer.DeserializeAsync<WsResponseMessage>(response);
        }
    }

    public class WebSocketManager : IDisposable
    {
        private const int MaxBufferSize = 10240;
        private ClientWebSocket clientWebSocket = null;
        private byte[] receiveBytes = new byte[MaxBufferSize];

        public void Dispose()
        {
            try
            {
                this.CloseAsync().Wait();
            }
            catch (ObjectDisposedException)
            {
            }
            catch (AggregateException ae)
            {
                ae.Handle(
                    ex =>
                    {
                        return true;
                    });
            }
        }

        public async Task<bool> ConnectAsync(Uri serviceAddress)
        {
            this.clientWebSocket = new ClientWebSocket();

            using (CancellationTokenSource tcs = new CancellationTokenSource(TimeSpan.FromSeconds(5)))
            {
                await this.clientWebSocket.ConnectAsync(serviceAddress, tcs.Token);
            }

            return true;
        }

        public async Task<byte[]> SendReceiveAsync(byte[] payload)
        {
            try
            {
                // Send request operation
                await this.clientWebSocket.SendAsync(new ArraySegment<byte>(payload), WebSocketMessageType.Binary, true, CancellationToken.None);

                WebSocketReceiveResult receiveResult =
                    await this.clientWebSocket.ReceiveAsync(new ArraySegment<byte>(this.receiveBytes), CancellationToken.None);

                using (MemoryStream ms = new MemoryStream())
                {
                    await ms.WriteAsync(this.receiveBytes, 0, receiveResult.Count);
                    return ms.ToArray();
                }
            }
            catch (WebSocketException exweb)
            {
                this.CloseAsync().Wait();
            }

            return null;
        }

        public async Task CloseAsync()
        {
            try
            {
                if (this.clientWebSocket != null)
                {
                    if (this.clientWebSocket.State != WebSocketState.Closed)
                    {
                        await this.clientWebSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);
                    }
                    this.clientWebSocket.Dispose();
                }
            }
            catch (Exception ex)
            {
            }
            finally
            {
                this.clientWebSocket = null;
            }
        }
    }
}
