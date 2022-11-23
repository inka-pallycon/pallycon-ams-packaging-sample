using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Claims;

using Microsoft.Azure.Management.Media;
using Microsoft.Azure.Management.Media.Models;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;

using PallyCon;

namespace PallyCon
{
    class Program
    {
        private const string AdaptiveStreamingTransformName = "PallyConSampleTransformWithAdaptiveStreamingPreset";
        private const string SourceUri = "";    // Your content
        private static readonly string ContentId = "";  // Your content id
        private static readonly string DefaultStreamingEndpointName = "default";     // Change this to your Endpoint name.
        private static readonly string StreamingPolicyName = "cbcsStreamingPolicy";
        private static readonly string LabelDefaultKey = "cbcsKeyDefault";
        public static async Task Main(string[] args)
        {

            ConfigWrapper config = new ConfigWrapper(new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
                .AddEnvironmentVariables()
                .Build());

            try
            {
                await RunAsync(config);
            }
            catch (Exception e)
            {
                Console.Error.WriteLine($"{e.Message}");


                if (e.GetBaseException() is ErrorResponseException apiException)
                {
                    Console.Error.WriteLine(
                        $"ERROR: API call failed with error code '{apiException.Body.Error.Code}' and message '{apiException.Body.Error.Message}'.");
                }
            }

            Console.WriteLine("Press Enter to continue.");
            Console.ReadLine();
        }

        private static async Task RunAsync(ConfigWrapper config)
        {
            IAzureMediaServicesClient client;

            try
            {
                client = await Authentication.CreateMediaServicesClientAsync(config);
                Console.WriteLine("connected");
            }
            catch (Exception e)
            {
                Console.Error.WriteLine("TIP: Make sure that you have filled out the appsettings.json file before running this sample.");
                Console.Error.WriteLine($"{e.Message}");
                return;
            }

            // Set the polling interval for long running operations to 2 seconds.
            // The default value is 30 seconds for the .NET client SDK
            client.LongRunningOperationRetryTimeout = 2;

            // Creating a unique suffix so that we don't have name collisions if you run the sample
            // multiple times without cleaning up.
            string uniqueness = Guid.NewGuid().ToString("N");
            string jobName = $"job-{uniqueness}";
            string locatorName = $"locator-{uniqueness}";
            string outputAssetName = $"output-{uniqueness}";
            bool stopEndpoint = false;

            // In this sample, we use polling the job for status
            // For production ready code, it is always recommended to use Event Grid instead of polling on the Job status. 
            try
            {
                // Ensure that you have the desired encoding Transform. This is really a one time setup operation.
                Transform transform = await GetOrCreateTransformAsync(client, config.ResourceGroup, config.AccountName, AdaptiveStreamingTransformName);

                // Output from the encoding Job must be written to an Asset, so let's create one
                Asset outputAsset = await CreateOutputAssetAsync(client, config.ResourceGroup, config.AccountName, outputAssetName);

                Job job = await SubmitJobAsync(client, config.ResourceGroup, config.AccountName, AdaptiveStreamingTransformName, outputAsset.Name, jobName);
                
                Console.WriteLine("Polling job status...");
                job = await WaitForJobToFinishAsync(client, config.ResourceGroup, config.AccountName, AdaptiveStreamingTransformName, jobName);

                if (job.State == JobState.Finished)
                {
                    string hls_key_uri = "";
                    var cbcsKey = PallyConHelper.GetCbcsKeyFromPallyCon(config.PallyConKmsUrl, config.PallyConEncToken, ContentId, ref hls_key_uri);
                    cbcsKey.LabelReferenceInStreamingPolicy = LabelDefaultKey;

                    // Create the streaming policy
                    StreamingPolicy streamingPolicy = await GetOrCreateStreamingPolicyAsync(client, config.ResourceGroup, config.AccountName
                        , StreamingPolicyName, hls_key_uri);

                    // Sets StreamingLocator.StreamingPolicyName to above policy.
                    StreamingLocator streamingLocator = await CreateStreamingLocatorAsync(client, config.ResourceGroup, config.AccountName, outputAsset.Name
                        , locatorName, streamingPolicy.Name, ContentId, cbcsKey);

                    StreamingEndpoint streamingEndpoint = await client.StreamingEndpoints.GetAsync(config.ResourceGroup, config.AccountName,
                        DefaultStreamingEndpointName);

                    if (streamingEndpoint.ResourceState != StreamingEndpointResourceState.Running)
                    {
                        Console.WriteLine("Streaming Endpoint was Stopped, restarting now..");
                        await client.StreamingEndpoints.StartAsync(config.ResourceGroup, config.AccountName, DefaultStreamingEndpointName);

                        // Since we started the endpoint, we should stop it in cleanup.
                        stopEndpoint = true;
                    }

                    string hlsPath = await GetHlsStreamingUrlAsync(client, config.ResourceGroup, config.AccountName, streamingLocator.Name, streamingEndpoint);

                    Console.WriteLine();
                    Console.WriteLine("HLS url can be played on your Apple device:");
                    Console.WriteLine(hlsPath);
                    Console.WriteLine();
                }

                Console.WriteLine("When finished testing press enter to cleanup.");
                Console.Out.Flush();
                Console.ReadLine();
            }
            catch (ErrorResponseException e)
            {
                Console.WriteLine("Hit ErrorResponseException");
                Console.WriteLine($"\tCode: {e.Body.Error.Code}");
                Console.WriteLine($"\tMessage: {e.Body.Error.Message}");
                Console.WriteLine();
                Console.WriteLine("Exiting, cleanup may be necessary...");
                Console.ReadLine();
            }
            finally
            {
                Console.WriteLine("Cleaning up...");
                await CleanUpAsync(client, config.ResourceGroup, config.AccountName, AdaptiveStreamingTransformName, outputAssetName,
                    jobName, stopEndpoint, DefaultStreamingEndpointName, StreamingPolicyName);
            }
        }

        /// <summary>
        /// If the specified transform exists, get that transform.
        /// If the it does not exist, creates a new transform with the specified output. 
        /// In this case, the output is set to encode a video using one of the built-in encoding presets.
        /// </summary>
        /// <param name="client">The Media Services client.</param>
        /// <param name="resourceGroupName">The name of the resource group within the Azure subscription.</param>
        /// <param name="accountName"> The Media Services account name.</param>
        /// <param name="transformName">The name of the transform.</param>
        /// <returns></returns>
        private static async Task<Transform> GetOrCreateTransformAsync(
            IAzureMediaServicesClient client,
            string resourceGroupName,
            string accountName,
            string transformName)
        {
            // You need to specify what you want it to produce as an output
            TransformOutput[] output = new TransformOutput[]
            {
                new TransformOutput
                {
                    // The preset for the Transform is set to one of Media Services built-in sample presets.
                    // You can  customize the encoding settings by changing this to use "StandardEncoderPreset" class.
                    Preset = new BuiltInStandardEncoderPreset()
                    {
                        // This sample uses the built-in encoding preset for Adaptive Bitrate Streaming.
                        PresetName = EncoderNamedPreset.AdaptiveStreaming
                    }
                }
            };

            // Create the Transform with the output defined above
            Console.WriteLine("Creating a transform...");
            // Does a Transform already exist with the desired name? This method will just overwrite (Update) the Transform if it exists already. 
            // In production code, you may want to be cautious about that. It really depends on your scenario.
            Transform transform = await client.Transforms.CreateOrUpdateAsync(resourceGroupName, accountName, transformName, output);

            return transform;
        }


        /// <summary>
        /// Creates an output asset. The output from the encoding Job must be written to an Asset.
        /// </summary>
        /// <param name="client">The Media Services client.</param>
        /// <param name="resourceGroupName">The name of the resource group within the Azure subscription.</param>
        /// <param name="accountName"> The Media Services account name.</param>
        /// <param name="assetName">The output asset name.</param>
        /// <returns></returns>
        private static async Task<Asset> CreateOutputAssetAsync(IAzureMediaServicesClient client, string resourceGroupName, string accountName, string assetName)
        {
            Asset asset = new();

            Console.WriteLine("Creating an output asset...");
            return await client.Assets.CreateOrUpdateAsync(resourceGroupName, accountName, assetName, asset);
        }

        /// <summary>
        /// Submits a request to Media Services to apply the specified Transform to a given input video.
        /// </summary>
        /// <param name="client">The Media Services client.</param>
        /// <param name="resourceGroupName">The name of the resource group within the Azure subscription.</param>
        /// <param name="accountName"> The Media Services account name.</param>
        /// <param name="transformName">The name of the transform.</param>
        /// <param name="outputAssetName">The (unique) name of the  output asset that will store the result of the encoding job. </param>
        /// <param name="jobName">The (unique) name of the job.</param>
        /// <returns></returns>
        private static async Task<Job> SubmitJobAsync(IAzureMediaServicesClient client,
            string resourceGroup,
            string accountName,
            string transformName,
            string outputAssetName,
            string jobName)
        {
            // This example shows how to encode from any HTTPs source URL - a new feature of the v3 API.  
            // Change the URL to any accessible HTTPs URL or SAS URL from Azure.
            JobInputHttp jobInput =
                new(files: new[] { SourceUri });

            JobOutput[] jobOutputs =
            {
                new JobOutputAsset(outputAssetName),
            };

            // In this example, we are assuming that the job name is unique.
            //
            // If you already have a job with the desired name, use the Jobs.Get method
            // to get the existing job. In Media Services v3, the Get method on entities returns null 
            // if the entity doesn't exist (a case-insensitive check on the name).
            Console.WriteLine("Creating a job...");
            Job job = await client.Jobs.CreateAsync(
                resourceGroup,
                accountName,
                transformName,
                jobName,
                new Job
                {
                    Input = jobInput,
                    Outputs = jobOutputs,
                });

            return job;
        }


        /// <summary>
        /// Polls Media Services for the status of the Job.
        /// </summary>
        /// <param name="client">The Media Services client.</param>
        /// <param name="resourceGroupName">The name of the resource group within the Azure subscription.</param>
        /// <param name="accountName"> The Media Services account name.</param>
        /// <param name="transformName">The name of the transform.</param>
        /// <param name="jobName">The name of the job you submitted.</param>
        /// <returns></returns>
        private static async Task<Job> WaitForJobToFinishAsync(IAzureMediaServicesClient client,
            string resourceGroupName,
            string accountName,
            string transformName,
            string jobName)
        {
            const int SleepIntervalMs = 30 * 1000;

            Job job;

            do
            {
                job = await client.Jobs.GetAsync(resourceGroupName, accountName, transformName, jobName);

                Console.WriteLine($"Job is '{job.State}'.");
                for (int i = 0; i < job.Outputs.Count; i++)
                {
                    JobOutput output = job.Outputs[i];
                    Console.Write($"\tJobOutput[{i}] is '{output.State}'.");
                    if (output.State == JobState.Processing)
                    {
                        Console.Write($"  Progress: '{output.Progress}'.");
                    }

                    Console.WriteLine();
                }

                if (job.State != JobState.Finished && job.State != JobState.Error && job.State != JobState.Canceled)
                {
                    await Task.Delay(SleepIntervalMs);
                }
            }
            while (job.State != JobState.Finished && job.State != JobState.Error && job.State != JobState.Canceled);

            return job;
        }

        /// <summary>
        /// Get or create a custom streaming policy for FairPlay.
        /// </summary>
        /// <param name="client">The Media Services client.</param>
        /// <param name="resourceGroupName">The name of the resource group within the Azure subscription.</param>
        /// <param name="accountName"> The Media Services account name.</param>
        /// <param name="streamingPolicyName">The streaming policy name.</param>
        /// <param name="hlsKeyUri">The key uri value to be set in m3u8 manifest.</param>
        /// <returns>StreamingPolicy</returns>
        private static async Task<StreamingPolicy> GetOrCreateStreamingPolicyAsync(IAzureMediaServicesClient client,
            string resourceGroupName, string accountName, string streamingPolicyName, string hlsKeyUri)
        {
            // In Media Services v3, the Get method on entities will return an ErrorResponseException if the resource is not found. 
            bool createPolicy = false;
            StreamingPolicy streamingPolicy = null;

            try
            {
                streamingPolicy = await client.StreamingPolicies.GetAsync(resourceGroupName, accountName, streamingPolicyName);
                Console.WriteLine($"Warning: The streaming policy named {streamingPolicyName} already exists.");
            }
            catch (ErrorResponseException ex) when (ex.Response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                // Streaming policy does not exist
                createPolicy = true;
            }

            if (createPolicy)
            {
                streamingPolicy = new StreamingPolicy
                {
                    CommonEncryptionCbcs = new CommonEncryptionCbcs()
                    {
                        Drm = new CbcsDrmConfiguration()
                        {
                            FairPlay = new StreamingPolicyFairPlayConfiguration()
                            {
                                CustomLicenseAcquisitionUrlTemplate = hlsKeyUri
                            }
                        },
                        EnabledProtocols = new EnabledProtocols()
                        {
                            Hls = true,
                            Dash = true // Even though DASH under CBCS is not supported for either CSF or CMAF, HLS-CMAF-CBCS uses DASH-CBCS fragments in its HLS playlist
                        },
                        ContentKeys = new StreamingPolicyContentKeys()
                        {
                            //Default key must be specified if keyToTrackMappings is present
                            DefaultKey = new DefaultKey()
                            {
                                Label = LabelDefaultKey
                            }
                        }
                    }
                };

                streamingPolicy = await client.StreamingPolicies.CreateAsync(resourceGroupName, accountName, streamingPolicyName, streamingPolicy);
            }

            return streamingPolicy;
        }

        /// <summary>
        /// Creates a StreamingLocator for the specified asset and with the specified streaming policy name.
        /// Once the StreamingLocator is created the output asset is available to clients for playback.
        /// 
        /// </summary>
        /// <param name="client">The Media Services client.</param>
        /// <param name="resourceGroup">The name of the resource group within the Azure subscription.</param>
        /// <param name="accountName"> The Media Services account name.</param>
        /// <param name="assetName">The name of the output asset.</param>
        /// <param name="locatorName">The StreamingLocator name (unique in this case).</param>
        /// <param name="customStreamingPolicyName">The StreamingPolicy name to be associated with.</param>
        /// <param name="contentId">The content id.</param>
        /// <param name="cbcsKey">The content key.</param>
        /// <returns></returns>
        private static async Task<StreamingLocator> CreateStreamingLocatorAsync(
            IAzureMediaServicesClient client,
            string resourceGroup,
            string accountName,
            string assetName,
            string locatorName,
            string customStreamingPolicyName,
            string contentId,
            StreamingLocatorContentKey cbcsKey)
        {
            StreamingLocator locator;

            // Let's check if the locator exists already
            try
            {
                locator = await client.StreamingLocators.GetAsync(resourceGroup, accountName, locatorName);
            }
            catch (ErrorResponseException ex) when (ex.Response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                // Name collision! This should not happen in this sample. If it does happen, in order to get the sample to work,
                // let's just go ahead and create a unique name.
                // Note that the returned locatorName can have a different name than the one specified as an input parameter.
                // You may want to update this part to throw an Exception instead, and handle name collisions differently.
                Console.WriteLine("Warning – found an existing Streaming Locator with name = " + locatorName);

                string uniqueness = $"-{Guid.NewGuid():N}";
                locatorName += uniqueness;

                Console.WriteLine("Creating a Streaming Locator with this name instead: " + locatorName);
            }
            
            var customLocator = new StreamingLocator(
                assetName,
                customStreamingPolicyName,
                contentKeys: new List<StreamingLocatorContentKey> { cbcsKey },
                streamingLocatorId: Guid.NewGuid(),
                alternativeMediaId: contentId
            );

            locator = await client.StreamingLocators.CreateAsync(
                resourceGroup,
                accountName,
                locatorName,
                customLocator
                );

            return locator;
        }

        /// <summary>
        /// Checks if the "default" streaming endpoint is in the running state,
        /// if not, starts it.
        /// Then, builds the streaming URLs.
        /// </summary>
        /// <param name="client">The Media Services client.</param>
        /// <param name="resourceGroupName">The name of the resource group within the Azure subscription.</param>
        /// <param name="accountName"> The Media Services account name.</param>
        /// <param name="locatorName">The name of the StreamingLocator that was created.</param>
        /// <param name="streamingEndpoint">The streaming endpoint.</param>
        /// <returns></returns>
        private static async Task<string> GetHlsStreamingUrlAsync(
            IAzureMediaServicesClient client,
            string resourceGroupName,
            string accountName,
            string locatorName,
            StreamingEndpoint streamingEndpoint)
        {
            string hlsPath = "";

            ListPathsResponse paths = await client.StreamingLocators.ListPathsAsync(resourceGroupName, accountName, locatorName);

            foreach (StreamingPath path in paths.StreamingPaths)
            {
                UriBuilder uriBuilder = new()
                {
                    Scheme = "https",
                    Host = streamingEndpoint.HostName
                };

                if (path.StreamingProtocol == StreamingPolicyStreamingProtocol.Hls)
                {
                    uriBuilder.Path = path.Paths[0];
                    hlsPath = uriBuilder.ToString();
                }
            }

            return hlsPath;
        }

        /// <summary>
        /// Deletes the jobs and assets that were created.
        /// Generally, you should clean up everything except objects 
        /// that you are planning to reuse (typically, you will reuse Transforms, and you will persist StreamingLocators).
        /// </summary>
        /// <param name="client">The Media Services client.</param>
        /// <param name="resourceGroupName">The name of the resource group within the Azure subscription.</param>
        /// <param name="accountName"> The Media Services account name.</param>
        /// <param name="transformName">The transform name.</param>
        /// <param name="assetName">The output asset name</param>
        /// <param name="jobName">The job name.</param>
        /// <param name="stopEndpoint">Stop endpoint if true, keep endpoint running if false.</param>
        /// <param name="streamingEndpointName">The endpoint name.</param>
        /// <param name="streamingPolicyName">The streaming policy name.</param>
        private static async Task CleanUpAsync(
            IAzureMediaServicesClient client,
            string resourceGroupName,
            string accountName,
            string transformName,
            string assetName,
            string jobName,
            bool stopEndpoint,
            string streamingEndpointName,
            string streamingPolicyName
            )
        {
            await client.Assets.DeleteAsync(resourceGroupName, accountName, assetName);

            await client.Jobs.DeleteAsync(resourceGroupName, accountName, transformName, jobName);

            await client.StreamingPolicies.DeleteAsync(resourceGroupName, accountName, streamingPolicyName);

            if (stopEndpoint)
            {
                // Because we started the endpoint, we'll stop it.
                await client.StreamingEndpoints.StopAsync(resourceGroupName, accountName, streamingEndpointName);
            }
            else
            {
                // We will keep the endpoint running because it was not started by us. There are costs to keep it running.
                // Please refer https://azure.microsoft.com/en-us/pricing/details/media-services/ for pricing. 
                Console.WriteLine($"The endpoint {streamingEndpointName} is running. To halt further billing on the endpoint, please stop it in azure portal or AMS Explorer.");
            }
        }
    }
}