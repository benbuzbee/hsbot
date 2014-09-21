using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HSBot.Cards
{
    class DebugPaster
    {
        private static readonly String API_KEY = "f94e256713c8bbf78c5ee5277ce0217e";

        public static async Task<String> PasteCard(Card c)
        {
            var httpclient = new System.Net.Http.HttpClient();
            var content = new System.Net.Http.MultipartFormDataContent();
            Dictionary<String, String> formData = new Dictionary<String, String>();

            content.Add(new System.Net.Http.StringContent(API_KEY), "api_dev_key");
            content.Add(new System.Net.Http.StringContent("paste"), "api_option");
            content.Add(new System.Net.Http.StringContent(String.Format("Debug data for {0}", c.Name)), "api_paste_name");
            content.Add(new System.Net.Http.StringContent(String.Format("{0}", c.XmlData)), "api_paste_code");
            
            
            var response = await httpclient.PostAsync("http://pastebin.com/api/api_post.php", content);

            if (!response.IsSuccessStatusCode)
                return null;

            return await response.Content.ReadAsStringAsync();


         //   var request = System.Net.HttpWebRequest.Create("http://pastebin.com/api/api_post.php");
           // request.Method = "POST";
        }

    }
}
