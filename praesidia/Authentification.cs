using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Policy;
using System.Text;
using System.Threading.Tasks;
using System.Web;
using System.Windows;
using Newtonsoft.Json.Linq;

namespace MRI.praesidia
{
    class Authentication
    {

        public static async Task LaunchWithCookie(string account_token, string place_id, string friend_id = "")
        {
            CookieContainer cookieContainer = new CookieContainer();
            HttpClientHandler handler = new HttpClientHandler()
            {
                CookieContainer = cookieContainer,
                UseCookies = true,
                AllowAutoRedirect = false
            };
            // Adding your token to session:
            var cookie = new Cookie(".ROBLOSECURITY", account_token)
            {
                Domain = ".roblox.com",
                Path = "/"
            };
            cookieContainer.Add(cookie);

            HttpClient client = new HttpClient(handler);

            var auth_ticket_pre = new HttpRequestMessage(HttpMethod.Post, "https://auth.roblox.com/v1/authentication-ticket");
            var auth_ticket_pre_response = await client.SendAsync(auth_ticket_pre);

            string csrf_token = null;
            if (auth_ticket_pre_response.Headers.TryGetValues("x-csrf-token", out var values))
            {
                csrf_token = string.Join(", ", values);
                Debug.WriteLine("x-csrf-token: " + string.Join(", ", values));
            }
            else
            {
                MessageBox.Show("Couldn't get CSRF Token.");
                return;
            }

            var auth_ticket_request = new HttpRequestMessage(HttpMethod.Post, "https://auth.roblox.com/v1/authentication-ticket");
            auth_ticket_request.Headers.Add("Referer", "https://www.roblox.com/home");
            auth_ticket_request.Headers.Add("Origin", "https://www.roblox.com");
            auth_ticket_request.Headers.Add("X-CSRF-TOKEN", csrf_token);
            auth_ticket_request.Content = new StringContent("", System.Text.Encoding.UTF8, "application/json");

            var auth_ticket_response = await client.SendAsync(auth_ticket_request);

            string auth_ticket = null;
            if (auth_ticket_response.Headers.TryGetValues("rbx-authentication-ticket", out var value))
            {
                auth_ticket = string.Join(", ", value);
                Debug.WriteLine("rbx-authentication-ticket: " + string.Join(", ", value));
            }
            else
            {
                MessageBox.Show("Couldn't get Auth Ticket.");
                Debug.WriteLine(auth_ticket_response.Headers.ToString());
                Debug.WriteLine(auth_ticket_response.StatusCode.ToString());
                Debug.WriteLine(auth_ticket_response.Content.ToString());
                return;
            }

            long launch_time = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            Guid random_uuid = Guid.NewGuid();
            string uuid = random_uuid.ToString();

            string url = null;
            if (friend_id == "" || friend_id == "...") {
                url = $"https://www.roblox.com/Game/PlaceLauncher.ashx?request=RequestGame&browserTrackerId=0&placeId={place_id}&isPlayTogetherGame=false&joinAttemptId={uuid}&joinAttemptOrigin=PlayButton";
            }else
            {
                url = $"https://www.roblox.com/Game/PlaceLauncher.ashx?request=RequestFollowUser&browserTrackerId=0&userId={friend_id}&joinAttemptId={uuid}&joinAttemptOrigin=JoinUser";
            }

            string launch_url = $"roblox-player:1+launchmode:play+gameinfo:{auth_ticket}+launchtime:{launch_time}+placelauncherurl:{HttpUtility.UrlEncode(url)}+browsertrackerid:0+robloxLocale:en_us+gameLocale:en_us+channel:+LaunchExp:InApp";

            Process.Start(new ProcessStartInfo
            {
                FileName = launch_url,
                UseShellExecute = true
            });
        }

    }
}
