using knkwebapi_v2.Attributes;

namespace knkwebapi_v2.Models;

[FormConfigurableEntity("Grade")]
public class Grade
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int Stars { get; set; }
}
