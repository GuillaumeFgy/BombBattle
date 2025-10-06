using System;
using Unity.Netcode;
using UnityEngine;

public struct PlayerStats : INetworkSerializable, IEquatable<PlayerStats>
{

    public ulong playerId;
    public int score;

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter 
    {
        serializer.SerializeValue(ref playerId);
        serializer.SerializeValue(ref score);
    }

    public bool Equals(PlayerStats other) 
    {
        return playerId == other.playerId;
    }
}
