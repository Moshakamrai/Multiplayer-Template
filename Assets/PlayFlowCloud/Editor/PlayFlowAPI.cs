using System;
using System.Collections.Generic;
using System.Net;
using UnityEditor;
using UnityEngine;

public class PlayFlowAPI
{
    // Determine if we're in development or production based on Unity Editor
    // private static string API_URL = "http://localhost:8000";
    private static string API_URL =  "https://api.computeflow.cloud";
    public class PlayFlowWebClient : WebClient
    {
        protected override WebRequest GetWebRequest(Uri address)
        {
            var request = base.GetWebRequest(address);
            request.Timeout = 10000000;
            return request;
        }
    }

    [System.Serializable]
    private class ProjectIdResponse
    {
        public string project_id;
    }
    
    public static string GetProjectID(string apiKey)
    {
        string projectID = "";
        try
        {
            string actionUrl = $"{API_URL}/v2/project";

            using (PlayFlowWebClient client = new PlayFlowWebClient())
            {
                client.Headers.Add("api-key", apiKey);
                string response = client.DownloadString(actionUrl);
                var responseData = JsonUtility.FromJson<ProjectIdResponse>(response);
                projectID = responseData.project_id;
            }
        }
        catch (Exception e)
        {
            Debug.Log(e);
        }
        return projectID;
    }

    public static string Upload(string fileLocation, string apiKey, string buildName = "default")
    {
        string output = "";
        try
        {
            //Display a progress bar 
            EditorUtility.DisplayProgressBar("Uploading to PlayFlow", "Uploading build to server", 0.75f);
            string actionUrl = $"{API_URL}/v2/builds/builds/upload?name={System.Uri.EscapeDataString(buildName)}";

            byte[] responseArray;
            using (PlayFlowWebClient client = new PlayFlowWebClient())
            {
                client.Headers.Add("api-key", apiKey);

                responseArray = client.UploadFile(actionUrl, fileLocation);

                output = (System.Text.Encoding.UTF8.GetString(responseArray));
            }
        }
        catch (Exception e)
        {
            EditorUtility.ClearProgressBar();
            Debug.Log(e);
        }
        return output;
    }
}
