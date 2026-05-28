using UnityEngine;

public static class WorkshopSocketUtility
{
    public static WorkshopPart GetDirectOccupant(PartSocket socket)
    {
        if (socket == null) return null;
        foreach (Transform child in socket.transform)
        {
            if (child != null && child.TryGetComponent(out WorkshopPart wp))
                return wp;
        }
        return null;
    }

    public static bool IsOccupied(PartSocket socket)
    {
        return GetDirectOccupant(socket) != null;
    }
}
