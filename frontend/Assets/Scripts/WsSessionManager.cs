using UnityEngine;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Net.WebSockets;
using System.Linq;
using shared;
using Google.Protobuf;

public class WsSessionManager {
    public delegate void OnWsSessionEvtCallbackType(int resultCode);

    // Reference https://github.com/paulbatum/WebSocket-Samples/blob/master/HttpListenerWebSocketEcho/Client/Client.cs
    private const int receiveChunkSize = 64;

    /**
    A "Queue" is in general NOT thread-safe, but when "bool TryDequeue(out T)" is always called in "thread#A" while "void Enqueue(T)" being always called in "thread#B", we're safe, e.g. "thread#A == UIThread && thread#B == WebSocketThread" or viceversa. 
        
    I'm not using "RecvRingBuff" from https://github.com/genxium/DelayNoMore/blob/v1.0.14/frontend/build-templates/jsb-link/frameworks/runtime-src/Classes/ring_buff.cpp because WebSocket is supposed to be preserving send/recv order at all time.
    */
    public Queue<WsReq> senderBuffer;
    public Queue<WsResp> recvBuffer;
    private string authToken;
    private int playerId = shared.Battle.TERMINATING_PLAYER_ID;

    private static WsSessionManager _instance;

    public static WsSessionManager Instance {
        get {
            if (null == _instance) _instance = new WsSessionManager();
            return _instance;
        }
    }

    private WsSessionManager() {
        ClearCredentials();
        senderBuffer = new Queue<WsReq>();
        recvBuffer = new Queue<WsResp>();
    }

    public void SetCredentials(string theAuthToken, int thePlayerId) {
        authToken = theAuthToken;
        playerId = thePlayerId;
    }

    public void ClearCredentials() {
        SetCredentials(null, Battle.TERMINATING_PLAYER_ID);
    }

    public async Task ConnectWsAsync(string wsEndpoint, CancellationToken cancellationToken, CancellationTokenSource cancellationTokenSource, OnWsSessionEvtCallbackType onOpen, OnWsSessionEvtCallbackType onClose) {
        if (null == authToken || shared.Battle.TERMINATING_PLAYER_ID == playerId) {
            Debug.Log(String.Format("ConnectWs not having enough credentials, authToken={0}, playerId={1}", authToken, playerId));
            return;
        }
        senderBuffer.Clear();
        recvBuffer.Clear();
        string fullUrl = wsEndpoint + String.Format("?authToken={0}&playerId={1}", authToken, playerId);
        using (ClientWebSocket ws = new ClientWebSocket()) {
            try {
                await ws.ConnectAsync(new Uri(fullUrl), cancellationTokenSource.Token);
                UnityEngine.WSA.Application.InvokeOnUIThread(() => {
                    onOpen(shared.ErrCode.Ok);
                }, true);
                await Task.WhenAll(Receive(ws, cancellationToken, cancellationTokenSource), Send(ws, cancellationToken));
                Debug.Log(String.Format("Both 'Receive' and 'Send' tasks are ended."));
            } catch (OperationCanceledException ocEx) {
                Debug.LogWarning(String.Format("WsSession is cancelled for 'ConnectAsync'; ocEx.Message={0}", ocEx.Message));
            }
        }
        UnityEngine.WSA.Application.InvokeOnUIThread(() => {
            onClose(shared.ErrCode.Ok);
        }, true);
    }

    private async Task Send(ClientWebSocket ws, CancellationToken cancellationToken) {
        Debug.Log(String.Format("Starts 'Send' loop, ws.State={0}", ws.State));
        WsReq toSendObj;
        try {
            while (WebSocketState.Open == ws.State && !cancellationToken.IsCancellationRequested) {
                while (senderBuffer.TryDequeue(out toSendObj) && !cancellationToken.IsCancellationRequested) {
                    await ws.SendAsync(new ArraySegment<byte>(toSendObj.ToByteArray()), WebSocketMessageType.Binary, true, cancellationToken);
                }
            }
        } catch (OperationCanceledException ocEx) {
            Debug.LogWarning(String.Format("WsSession is cancelled for 'Send'; ocEx.Message={0}", ocEx.Message));
        } finally {
            Debug.Log(String.Format("Ends 'Send' loop, ws.State={0}", ws.State));
        }
    }

    private async Task Receive(ClientWebSocket ws, CancellationToken cancellationToken, CancellationTokenSource cancellationTokenSource) {
        Debug.Log(String.Format("Starts 'Receive' loop, ws.State={0}, cancellationToken.IsCancellationRequested={1}", ws.State, cancellationToken.IsCancellationRequested));
        byte[] byteBuff = new byte[receiveChunkSize];
        try {
            while (WebSocketState.Open == ws.State && !cancellationToken.IsCancellationRequested) {
                Debug.Log(String.Format("'Receive' loop calling ReceiveAsync: ws.State={0}", ws.State));
                var result = await ws.ReceiveAsync(new ArraySegment<byte>(byteBuff), cancellationToken);
                Debug.Log(String.Format("'Receive' loop called ReceiveAsync: ws.State={0}, result.MessageType={1}", ws.State, result.MessageType));
                if (WebSocketMessageType.Close == result.MessageType) {
                    await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, string.Empty, CancellationToken.None);
                    Debug.Log(String.Format("WsSession is closed in 'Receive'"));
                    if (!cancellationToken.IsCancellationRequested) {
                        cancellationTokenSource.Cancel(); // To cancel the "Send" loop
                    }
                } else {
                    Debug.Log(String.Format("Received {0} bytes", result.Count));
                    try {
                        WsResp resp = WsResp.Parser.ParseFrom(byteBuff, 0, result.Count);
                        Debug.Log(String.Format("Received WsResp {0}", resp.ToString()));
                        recvBuffer.Enqueue(resp);
                    } catch (Exception pbEx) {
                        Debug.LogWarning(String.Format("Protobuf parser exception is caught for 'Receive'; ex.Message={0}", pbEx.Message));
                    }
                }
            }
        } catch (OperationCanceledException ocEx) {
            Debug.LogWarning(String.Format("WsSession is cancelled for 'Receive'; ocEx.Message={0}", ocEx.Message));
        } catch (Exception ex) {
            Debug.LogError(String.Format("WsSession is stopping for 'Receive' upon exception; ex.Message={0}", ex.Message));
        } finally {
            Debug.Log(String.Format("Ends 'Receive' loop, ws.State={0}", ws.State));
        }
    }

}
