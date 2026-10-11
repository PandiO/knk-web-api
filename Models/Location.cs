using System;
using knkwebapi_v2.Attributes;

namespace knkwebapi_v2.Models;

[FormConfigurableEntity("Location")]
public class Location
{
    public int Id { get; set; }
    public string? Name { get; set; } = "Location";
    public double X { get; set; }
    public double Y { get; set; }
    public double Z { get; set; }
    public float Yaw { get; set; }
    public float Pitch { get; set; }
    public string? World { get; set; } = "world";

    /// <summary>
    /// When the row was created (KNG-80): the orphan check skips Locations younger than its grace
    /// period, since a form often saves a Location before the entity that references it. Set by
    /// this initializer on every creation path; null for rows that existed before it was added,
    /// which the check treats as old. Not part of LocationDto, so updates never change it.
    /// </summary>
    public DateTime? CreatedAt { get; set; } = DateTime.UtcNow;
}
