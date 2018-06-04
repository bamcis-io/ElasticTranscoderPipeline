using Amazon.ElasticTranscoder;
using Amazon.ElasticTranscoder.Model;
using Amazon.Lambda.Core;
using BAMCIS.AWSLambda.Common;
using BAMCIS.AWSLambda.Common.Events;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

// Assembly attribute to enable the Lambda function's JSON input to be converted into a .NET class.
[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.Json.JsonSerializer))]

namespace BAMCIS.ElasticTranscoderPipeline
{
    /// <summary>
    /// The class that will be called by the lambda function.
    /// </summary>
    public class LambdaEntrypoint
    {
        #region Private Fields

        /// <summary>
        /// Placeholder for the lambda function context so
        /// other methods can access it to write logs
        /// </summary>
        private ILambdaContext _Context;

        /// <summary>
        /// The Elastic Transcoder client
        /// </summary>
        private IAmazonElasticTranscoder _ETClient = null;

        #endregion

        #region Constructors

        /// <summary>
        /// Default constructor that Lambda will invoke.
        /// </summary>
        public LambdaEntrypoint()
        {
            AmazonElasticTranscoderConfig Config = new AmazonElasticTranscoderConfig();
            this._ETClient = new AmazonElasticTranscoderClient(Config);
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Entrypoint for the Lambda function, calls the correct create, update, or delete function
        /// </summary>
        /// <param name="request">The custom resource request</param>
        /// <param name="context">The ILambdaContext object</param>
        /// <returns></returns>
        public async Task Init(CustomResourceRequest request, ILambdaContext context)
        {
            this._Context = context;
            this._Context.LogInfo($"Received request:\n{JsonConvert.SerializeObject(request)}");

            CustomResourceResponse Response = null;

            switch (request.RequestType)
            {
                case CustomResourceRequest.StackOperation.CREATE:
                    {
                        Response = await this.Create(request);
                        break;
                    }
                case CustomResourceRequest.StackOperation.DELETE:
                    {
                        Response = await this.Delete(request);
                        break;
                    }
                case CustomResourceRequest.StackOperation.UPDATE:
                    {
                        Response = await this.Update(request);
                        break;
                    }
                default:
                    {
                        throw new ArgumentException($"Unknown stack operation: {request.RequestType}.");
                    }
            }

            try
            {                
                JObject Obj = JObject.FromObject(Response);

                switch (request.RequestType)
                {
                    case CustomResourceRequest.StackOperation.CREATE:
                    case CustomResourceRequest.StackOperation.UPDATE:
                        {
                            if (Response.Status == CustomResourceResponse.RequestStatus.FAILED)
                            {
                                Obj.Remove("NoEcho");
                                Obj.Remove("Data");
                            }
                            
                            break;
                        }
                    case CustomResourceRequest.StackOperation.DELETE:
                        {
                            Obj.Remove("NoEcho");
                            Obj.Remove("Data");

                            if (Response.Status == CustomResourceResponse.RequestStatus.SUCCESS)
                            {
                                Obj.Remove("Reason");
                            }

                            break;
                        }
                    default:
                        {
                            this._Context.LogError($"Unknown request type: {request.RequestType}.");
                            break;
                        }
                }

                this._Context.LogInfo($"Attempting to send response to pre-signed s3 url: {Obj.ToString()}");

                HttpWebResponse FinalResponse = await UploadResponse(request.ResponseUrl, Obj.ToString());

                this._Context.LogInfo($"Submitted response with status code: {(int)FinalResponse.StatusCode}");
            }
            catch (HttpRequestException e)
            {
                this._Context.LogError(e);
            }
            catch (Exception e)
            {
                this._Context.LogError(e);
            }
        }

        #endregion

        #region Private Methods

        /// <summary>
        /// Creates a new Elastic Transcoder Pipeline
        /// </summary>
        /// <param name="request"></param>
        /// <returns></returns>
        private async Task<CustomResourceResponse> Create(CustomResourceRequest request)
        {
            try
            {
                this._Context.LogInfo("Attempting to create a pipeline.");
                CreatePipelineRequest PipelineRequest = JsonConvert.DeserializeObject<CreatePipelineRequest>(JsonConvert.SerializeObject(request.ResourceProperties));
                CreatePipelineResponse CreateResponse = await this._ETClient.CreatePipelineAsync(PipelineRequest);

                if ((int)CreateResponse.HttpStatusCode < 200 || (int)CreateResponse.HttpStatusCode > 299)
                {
                    return new CustomResourceResponse(CustomResourceResponse.RequestStatus.FAILED, $"Received HTTP status code {(int)CreateResponse.HttpStatusCode}.", request);
                }
                else
                {
                    return new CustomResourceResponse(
                        CustomResourceResponse.RequestStatus.SUCCESS,
                        $"See the details in CloudWatch Log Stream: {this._Context.LogStreamName}.",
                        CreateResponse.Pipeline.Id,
                        request.StackId,
                        request.RequestId,
                        request.LogicalResourceId,
                        false,
                        new Dictionary<string, object>()
                        {
                            {"Name", CreateResponse.Pipeline.Name },
                            {"Arn", CreateResponse.Pipeline.Arn },
                            {"Id", CreateResponse.Pipeline.Id }
                        }
                    );
                }
            }
            catch (AmazonElasticTranscoderException e)
            {
                this._Context.LogError(e);

                return new CustomResourceResponse(
                    CustomResourceResponse.RequestStatus.FAILED,
                    e.Message,
                    Guid.NewGuid().ToString(),
                    request.StackId,
                    request.RequestId,
                    request.LogicalResourceId
                );
            }
            catch (Exception e)
            {
                this._Context.LogError(e);

                return new CustomResourceResponse(
                    CustomResourceResponse.RequestStatus.FAILED,
                    e.Message,
                    Guid.NewGuid().ToString(),
                    request.StackId,
                    request.RequestId,
                    request.LogicalResourceId
                );
            }
        }

        /// <summary>
        /// Deletes an Elastic Transcoder Pipeline
        /// </summary>
        /// <param name="request"></param>
        /// <returns></returns>
        private async Task<CustomResourceResponse> Delete(CustomResourceRequest request)
        {
            try
            {
                this._Context.LogInfo("Attempting to delete a pipeline.");

                ListPipelinesRequest Listing = new ListPipelinesRequest();

                List<Pipeline> Pipelines = new List<Pipeline>();
                ListPipelinesResponse Pipes;

                do
                {
                    Pipes = await this._ETClient.ListPipelinesAsync(Listing);

                    Pipelines.AddRange(Pipes.Pipelines.Where(x => x.Name.Equals(request.ResourceProperties["Name"] as string) &&
                        x.InputBucket.Equals(request.ResourceProperties["InputBucket"]) &&
                        x.Role.Equals(request.ResourceProperties["Role"])
                    ));

                } while (Pipes.NextPageToken != null);

                if (Pipelines.Count > 1)
                {
                    this._Context.LogWarning($"{Pipelines.Count} pipelines were found matching the Name, InputBucket, and Role specified.");
                }

                if (Pipelines.Count > 0)
                {
                    DeletePipelineRequest PipelineRequest = new DeletePipelineRequest()
                    {
                        Id = Pipelines.First().Id
                    };

                    DeletePipelineResponse DeleteResponse = await this._ETClient.DeletePipelineAsync(PipelineRequest);

                    if ((int)DeleteResponse.HttpStatusCode < 200 || (int)DeleteResponse.HttpStatusCode > 299)
                    {
                        return new CustomResourceResponse(CustomResourceResponse.RequestStatus.FAILED, $"Received HTTP status code {(int)DeleteResponse.HttpStatusCode}.", request);
                    }
                    else
                    {
                        return new CustomResourceResponse(
                            CustomResourceResponse.RequestStatus.SUCCESS,
                            $"See the details in CloudWatch Log Stream: {this._Context.LogStreamName}.",
                            request,
                            false
                        );
                    }
                }
                else
                {
                    return new CustomResourceResponse(
                        CustomResourceResponse.RequestStatus.SUCCESS,
                        "No pipelines could be found with the matching characteristics.",
                        request
                    );
                }
            }
            catch (AmazonElasticTranscoderException e)
            {
                // If the pipeline doesn't exist, consider it deleted
                if (e.StatusCode == HttpStatusCode.NotFound)
                {
                    return new CustomResourceResponse(
                        CustomResourceResponse.RequestStatus.SUCCESS,
                        $"See the details in CloudWatch Log Stream: {this._Context.LogStreamName}.",
                        request
                    );
                }
                else
                {
                    return new CustomResourceResponse(
                        CustomResourceResponse.RequestStatus.FAILED,
                        e.Message,
                        request
                    );
                }
            }
            catch (Exception e)
            {
                return new CustomResourceResponse(
                    CustomResourceResponse.RequestStatus.FAILED,
                    e.Message,
                    request
                );
            }
        }

        /// <summary>
        /// Updates an Elastic Transcoder Pipeline
        /// </summary>
        /// <param name="request"></param>
        /// <returns></returns>
        private async Task<CustomResourceResponse> Update(CustomResourceRequest request)
        {
            try
            {
                this._Context.LogInfo("Initiating update for pipeline.");

                UpdatePipelineRequest PipelineRequest = JsonConvert.DeserializeObject<UpdatePipelineRequest>(JsonConvert.SerializeObject(request.ResourceProperties));

                ListPipelinesRequest Listing = new ListPipelinesRequest();

                List<Pipeline> Pipelines = new List<Pipeline>();
                ListPipelinesResponse Pipes;

                do
                {
                    Pipes = await this._ETClient.ListPipelinesAsync(Listing);

                    Pipelines.AddRange(Pipes.Pipelines.Where(x => x.Name.Equals(request.ResourceProperties["Name"] as string) &&
                        x.InputBucket.Equals(request.ResourceProperties["InputBucket"]) &&
                        x.Role.Equals(request.ResourceProperties["Role"])
                    ));

                } while (Pipes.NextPageToken != null);

                if (Pipelines.Count > 1)
                {
                    this._Context.LogWarning($"{Pipelines.Count} pipelines were found matching the Name, InputBucket, and Role specified.");
                }

                if (Pipelines.Count > 0)
                {
                    PipelineRequest.Id = Pipelines.First().Id;

                    UpdatePipelineResponse UpdateResponse = await this._ETClient.UpdatePipelineAsync(PipelineRequest);

                    if ((int)UpdateResponse.HttpStatusCode < 200 || (int)UpdateResponse.HttpStatusCode > 299)
                    {
                        return new CustomResourceResponse(CustomResourceResponse.RequestStatus.FAILED, $"Received HTTP status code {(int)UpdateResponse.HttpStatusCode}.", request);
                    }
                    else
                    {
                        return new CustomResourceResponse(
                            CustomResourceResponse.RequestStatus.SUCCESS,
                            $"See the details in CloudWatch Log Stream: {this._Context.LogStreamName}.",
                            request,
                            false,
                            new Dictionary<string, object>()
                            {
                                {"Name", UpdateResponse.Pipeline.Name },
                                {"Arn", UpdateResponse.Pipeline.Arn },
                                {"Id", UpdateResponse.Pipeline.Id }
                            }
                        );
                    }
                }
                else
                {
                    return new CustomResourceResponse(
                        CustomResourceResponse.RequestStatus.FAILED,
                        "No pipelines could be found with the matching characteristics.",
                        request
                    );
                }
            }
            catch (AmazonElasticTranscoderException e)
            {
                return new CustomResourceResponse(
                    CustomResourceResponse.RequestStatus.FAILED,
                    e.Message,
                    request
                );
            }
            catch (Exception e)
            {
                return new CustomResourceResponse(
                    CustomResourceResponse.RequestStatus.FAILED,
                    e.Message,
                    request
                );
            }
        }

        /// <summary>
        /// Uploads the custom resource response to the pre-signed s3 url
        /// </summary>
        /// <param name="url">The pre-signed s3 url</param>
        /// <param name="content">The json serialized response</param>
        /// <returns></returns>
        private static async Task<HttpWebResponse> UploadResponse(Uri url, string content)
        {
            if (url == null)
            {
                throw new ArgumentNullException("url");
            }

            if (String.IsNullOrEmpty(content))
            {
                throw new ArgumentNullException("content");
            }

            /*
            HttpClient Client = new HttpClient();
            HttpRequestMessage Request = new HttpRequestMessage(HttpMethod.Put, url)
            {
                Content = new StringContent(content, Encoding.UTF8, "application/json")
            };

            return await Client.SendAsync(Request);
            */

            HttpWebRequest HttpRequest = WebRequest.Create(url) as HttpWebRequest;
            HttpRequest.Method = HttpMethod.Put.Method;

            byte[] Bytes = Encoding.UTF8.GetBytes(content);

            using (FileStream FStream = new FileStream("/tmp/temp.json", FileMode.Create))
            {
                FStream.Write(Bytes, 0, Bytes.Length);
            }

            using (Stream DataStream = HttpRequest.GetRequestStream())
            {
                byte[] Buffer = new byte[8192];
                using (FileStream FStream = new FileStream("/tmp/temp.json", FileMode.Open, FileAccess.Read))
                {
                    int BytesRead = 0;

                    while ((BytesRead = FStream.Read(Buffer, 0, Buffer.Length)) > 0)
                    {
                        DataStream.Write(Buffer, 0, BytesRead);
                    }
                }
            }

            File.Delete("/tmp/temp.json");

            HttpWebResponse Response = await HttpRequest.GetResponseAsync() as HttpWebResponse;
            return Response;
        }

        /// <summary>
        /// Uploads the custom resource response to the pre-signed s3 url
        /// </summary>
        /// <param name="url">The pre-signed s3 url</param>
        /// <param name="content">The json serialized response</param>
        /// <returns></returns>
        private static async Task<HttpWebResponse> UploadResponse(string url, string content)
        {
            return await UploadResponse(new Uri(url), content);
        }

        #endregion
    }
}
