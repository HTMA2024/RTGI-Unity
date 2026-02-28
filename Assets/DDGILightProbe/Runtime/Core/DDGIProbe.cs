using System;
using UnityEngine;

namespace DDGI
{

    public enum ProbeState
    {

        Active,

        Inactive,

        Sleeping
    }

    [Serializable]
    public class DDGIProbe
    {

        public Vector3Int gridIndex;

        public int flatIndex;

        public Vector3 position;

        public ProbeState state;

        public Vector2 irradianceAtlasUV;

        public Vector2 distanceAtlasUV;

        public Vector3 offset;

        public Vector3 ActualPosition => position + offset;

        public DDGIProbe()
        {
            gridIndex = Vector3Int.zero;
            flatIndex = 0;
            position = Vector3.zero;
            state = ProbeState.Active;
            irradianceAtlasUV = Vector2.zero;
            distanceAtlasUV = Vector2.zero;
            offset = Vector3.zero;
        }

        public DDGIProbe(Vector3Int gridIndex, int flatIndex, Vector3 position)
        {
            this.gridIndex = gridIndex;
            this.flatIndex = flatIndex;
            this.position = position;
            this.state = ProbeState.Active;
            this.irradianceAtlasUV = Vector2.zero;
            this.distanceAtlasUV = Vector2.zero;
            this.offset = Vector3.zero;
        }

        public void Reset()
        {
            state = ProbeState.Active;
            offset = Vector3.zero;
        }

        public void SetAtlasUV(Vector2 irradianceUV, Vector2 distanceUV)
        {
            irradianceAtlasUV = irradianceUV;
            distanceAtlasUV = distanceUV;
        }

        public override string ToString()
        {
            return $"Probe[{gridIndex}] Pos:{position} State:{state}";
        }
    }
}
