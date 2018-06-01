using Amazon.ElasticTranscoder;
using Amazon.ElasticTranscoder.Model;
using Amazon.Lambda.Core;
using BAMCIS.AWSLambda.Common;
using BAMCIS.AWSLambda.Common.Events;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
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
                HttpResponseMessage FinalResponse = await UploadResponse(request.ResponseUrl, JsonConvert.SerializeObject(Response));
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
                        request,
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
                return new CustomResourceResponse(
                    CustomResourceResponse.RequestStatus.FAILED,
                    JsonConvert.SerializeObject(e),
                    request
                );
            }
            catch (Exception e)
            {
                return new CustomResourceResponse(
                    CustomResourceResponse.RequestStatus.FAILED,
                    JsonConvert.SerializeObject(e),
                    request
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
                DeletePipelineRequest PipelineRequest = new DeletePipelineRequest()
                {
                    Id = request.PhysicalResourceId
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
                        JsonConvert.SerializeObject(e),
                        request
                    );
                }
            }
            catch (Exception e)
            {
                return new CustomResourceResponse(
                    CustomResourceResponse.RequestStatus.FAILED,
                    JsonConvert.SerializeObject(e),
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
                UpdatePipelineRequest PipelineRequest = JsonConvert.DeserializeObject<UpdatePipelineRequest>(JsonConvert.SerializeObject(request.ResourceProperties));
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
                        {"name", UpdateResponse.Pipeline.Name },
                        {"arn", UpdateResponse.Pipeline.Arn },
                        {"id", UpdateResponse.Pipeline.Id }
                        }
                    );
                }
            }
            catch (AmazonElasticTranscoderException e)
            {
                return new CustomResourceResponse(
                    CustomResourceResponse.RequestStatus.FAILED,
                    JsonConvert.SerializeObject(e),
                    request
                );
            }
            catch (Exception e)
            {
                return new CustomResourceResponse(
                    CustomResourceResponse.RequestStatus.FAILED,
                    JsonConvert.SerializeObject(e),
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
        private static async Task<HttpResponseMessage> UploadResponse(Uri url, string content)
        {
            if (url == null)
            {
                throw new ArgumentNullException("url");
            }

            if (String.IsNullOrEmpty(content))
            {
                throw new ArgumentNullException("content");
            }

            HttpClient Client = new HttpClient();
            HttpRequestMessage Request = new HttpRequestMessage(HttpMethod.Put, url)
            {
                Content = new StringContent(content)
            };

            return await Client.SendAsync(Request);
        }

        /// <summary>
        /// Uploads the custom resource response to the pre-signed s3 url
        /// </summary>
        /// <param name="url">The pre-signed s3 url</param>
        /// <param name="content">The json serialized response</param>
        /// <returns></returns>
        private static async Task<HttpResponseMessage> UploadResponse(string url, string content)
        {
            return await UploadResponse(new Uri(url), content);
        }

        #endregion
    }
}
