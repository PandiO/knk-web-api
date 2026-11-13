namespace knkwebapi_v2.Dtos
{
    /// <summary>
    /// PandiO/knk-workspace#269: a temp region the game server may remove now (<c>GET api/WorldTasks/temp-region-releases</c>).
    /// The API checked that nothing it knows of uses it; the game server checks again before removing.
    /// </summary>
    public class TempRegionReleaseDto
    {
        public int Id { get; set; }
        public int WorldTaskId { get; set; }
        /// <summary><c>tempregion_worldtask_&lt;WorldTaskId&gt;</c>.</summary>
        public string RegionId { get; set; } = null!;
        /// <summary>The world from the task's result, or null: look in every world.</summary>
        public string? World { get; set; }
        /// <summary>DraftDeleted, TaskCancelled, TaskExpired, TaskFailed or SessionRemoved.</summary>
        public string Reason { get; set; } = null!;
        public DateTime RequestedAt { get; set; }
    }

    /// <summary>#269: the game server's answer to a release (<c>POST api/WorldTasks/temp-region-releases/{id}/settle</c>).</summary>
    public class TempRegionReleaseSettleDto
    {
        /// <summary>Removed, Missing or Kept.</summary>
        public string? Outcome { get; set; }
        public string? ServerId { get; set; }
        /// <summary>Why it was kept, for the log (200 characters).</summary>
        public string? Detail { get; set; }
    }
}
