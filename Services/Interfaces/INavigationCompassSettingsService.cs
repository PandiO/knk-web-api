using System.Threading.Tasks;
using knkwebapi_v2.Dtos;

namespace knkwebapi_v2.Services.Interfaces
{
    /// <summary>The navigation compass item rule and its destinations (KNG-234) for the web-app navigation page and the game server.</summary>
    public interface INavigationCompassSettingsService
    {
        /// <summary>The saved settings, or the defaults while none were saved.</summary>
        Task<NavigationCompassSettingsDto> GetAsync();

        /// <summary>Saves the given fields (null leaves one). Bad material, name match or targets → ArgumentException.</summary>
        Task<NavigationCompassSettingsDto> UpdateAsync(UpdateNavigationCompassSettingsDto dto);
    }
}
