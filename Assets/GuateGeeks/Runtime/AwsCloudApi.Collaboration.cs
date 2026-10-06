using System;
using System.Collections;
using UnityEngine;

namespace GuateGeeks.AwsVr
{
    public sealed partial class AwsCloudApi
    {
        [Serializable] sealed class RoomBootstrap { public string roomId, name; public Architecture graph; public int tableSize; }
        [Serializable] sealed class TicketRequest { public string token; }
        [Serializable] public sealed class RoomTicket { public string ticket, websocketUrl; }
        public string RoomToken { get; set; }
        public void AdoptRoomDeployment(RoomDeployment deployment, Architecture graph)
        {
            if (deployment == null || !deployment.finished || !deployment.success || string.IsNullOrEmpty(deployment.stackId) || string.IsNullOrEmpty(deployment.fingerprint)) return;
            Slot = deployment.deploymentId; StackId = deployment.stackId;
            expectedHash = deployment.fingerprint; deployedGraph = graph.Copy();
        }
        public IEnumerator RefreshRoomDeployment(string slot)
        {
            yield return Request("GET", "/v1/deployments/" + slot, null, true, _ => { });
        }
        public IEnumerator CreateRoom(string code, string name, Architecture graph, TableSize table, Action<RoomGrant, string> complete)
        {
            CloudReply reply = null;
            yield return Request("POST", "/v1/collab/rooms", JsonUtility.ToJson(new RoomBootstrap { roomId = code, name = name, graph = graph, tableSize = (int)table }), false, r => reply = r);
            var grant = Parse<RoomGrant>(reply);
            complete(reply.Ok && !string.IsNullOrEmpty(grant?.token) ? grant : null, reply.Ok ? null : Describe(reply));
        }
        public IEnumerator GetRoomTicket(string token, Action<RoomTicket, string> complete)
        {
            CloudReply reply = null;
            yield return Request("POST", "/v1/collab/ticket", JsonUtility.ToJson(new TicketRequest { token = token }), false, r => reply = r);
            complete(reply.Ok ? Parse<RoomTicket>(reply) : null, reply.Ok ? null : Describe(reply));
        }
    }
}
