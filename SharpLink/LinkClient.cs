﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using SharpTox.Core;
using System.Net;
using Skynet.Models;
using System.Threading;
using Skynet.Utils;

namespace SharpLink
{
    class LinkClient
    {
        public string targetToxId;
        public ToxId serverToxId;
        public IPAddress ip;
        public int port;
        private Skynet.Base.Skynet mSkynet;
        public string clientId;
        public string serverId;
        private Action<byte[]> msgHandler;
        private Action<Exception> errorHander;
        private Action closeHandler;

        public LinkClient(Skynet.Base.Skynet mSkynet, string targetToxId, IPAddress ip, int port) {
            this.targetToxId = targetToxId;
            this.ip = ip;
            this.port = port;
            serverToxId = new ToxId(targetToxId);
            clientId = Guid.NewGuid().ToString();
            this.mSkynet = mSkynet;
        }

        private async Task<bool> HandShake() {
            bool status;
            var res = await mSkynet.sendRequest(serverToxId, new ToxRequest {
                url = "/handshake",
                method = "get",
                uuid = Guid.NewGuid().ToString(),
                fromNodeId = clientId,
                fromToxId = mSkynet.tox.Id.ToString(),
                toToxId = serverToxId.ToString(),
                toNodeId = "",
                time = Utils.UnixTimeNow(),
            }, out status);
            if (res == null)
                return false;
            else
                return true;
        }

        private async Task<bool> Connect() {
            mSkynet.addNewReqListener(newReqListener);
            bool status;
            string requuid = Guid.NewGuid().ToString();
            Console.WriteLine(Utils.UnixTimeNow() + " Start connect: " + requuid);
            var res = await mSkynet.sendRequest(new ToxId(targetToxId), new ToxRequest
            {
                url = "/connect",
                method = "get",
                uuid = requuid,
                fromNodeId = clientId,
                fromToxId = mSkynet.tox.Id.ToString(),
                toToxId = targetToxId,
                toNodeId = "",
                content=Encoding.UTF8.GetBytes(ip.ToString() + "\n" + port),
                time = Utils.UnixTimeNow(),
            }, out status);
            if (res == null || Encoding.UTF8.GetString(res.content) == "failed") {
                mSkynet.removeNewReqListener(newReqListener);
                return false;
            }
            Console.WriteLine(Utils.UnixTimeNow() + " Connect success: " + requuid);
            serverId = res.fromNodeId;
            return true;
        }

        public static LinkClient Connect(Skynet.Base.Skynet mSkynet, string targetToxId, IPAddress ip, int port) {
            LinkClient mLinkClient = new LinkClient(mSkynet, targetToxId, ip, port);
            Console.WriteLine( Utils.UnixTimeNow() + " Start Handshake, clientId: " + mLinkClient.clientId);
            var res = mLinkClient.HandShake().GetAwaiter().GetResult();
            Console.WriteLine(Utils.UnixTimeNow() + " End Handshake, clientId: " + mLinkClient.clientId);
            if (!res) {
                // 链接tox失败
                return null;
            }
            var connectRes = mLinkClient.Connect().GetAwaiter().GetResult();

            if (!connectRes) {
                // 创建socket失败
                return null;
            } 
            return mLinkClient;
        }

        public static LinkClient Connect(Skynet.Base.Skynet mSkynet, string targetToxId, string targetNodeID) {
            LinkClient mLinkClient = new LinkClient(mSkynet, targetToxId, null, 0);
            mLinkClient.serverId = targetNodeID;
            mLinkClient.serverToxId = new ToxId(targetToxId);
            mLinkClient.mSkynet.addNewReqListener(mLinkClient.newReqListener);
            return mLinkClient;
        }

        public bool Send(byte[] msg, int size) {
            bool status;
            mSkynet.sendRequestNoReplay(new ToxId(targetToxId), new ToxRequest
            {
                url = "/msg",
                method = "get",
                uuid = Guid.NewGuid().ToString(),
                fromNodeId = clientId,
                fromToxId = mSkynet.tox.Id.ToString(),
                toToxId = targetToxId,
                toNodeId = serverId,
                time = Skynet.Utils.Utils.UnixTimeNow(),
                content = msg.Take(size).ToArray(),
            }, out status);
            if (!status && errorHander != null)
                errorHander(new Exception("send message failed"));
            return status;
        }

        public bool Send(byte[] msg, int size, int retryCount) {
            int count = 0;
            while (count < retryCount) {
                var res = Send(msg, size);
                if (res)
                    break;
                count++;
                Thread.Sleep(10);
            }
            if (count == retryCount)
                return false;
            else
                return true;
        }

        public void OnMessage(Action<byte[]> msgHandler) {
            this.msgHandler = msgHandler;
        }

        public void newReqListener(ToxRequest req) {
            if (req.toNodeId == clientId && req.fromNodeId == serverId && req.url == "/msg") {
                msgHandler(req.content);
            }
            if (req.toNodeId == clientId && req.fromNodeId == serverId && req.url == "/close")
            {
                closeHandler();
                Close();
            }
        }

        public void Close() {
            mSkynet.removeNewReqListener(newReqListener);
        }

        public void CloseRemote() {
            bool status;
            mSkynet.sendRequestNoReplay(serverToxId, new ToxRequest {
                url = "/close",
                method = "get",
                uuid = Guid.NewGuid().ToString(),
                fromNodeId = clientId,
                fromToxId = mSkynet.tox.Id.ToString(),
                toToxId = serverToxId.ToString(),
                toNodeId = serverId,
                time = Skynet.Utils.Utils.UnixTimeNow(),
            }, out status);
        }

        public void OnClose(Action closeHandler) {
            this.closeHandler = closeHandler;
        }

        public void OnError(Action<Exception> errorHandler) {
            this.errorHander = errorHandler;
        }
    }
}
