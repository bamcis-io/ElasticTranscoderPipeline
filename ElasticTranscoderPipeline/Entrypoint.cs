using Amazon.ElasticTranscoder;
using Amazon.ElasticTranscoder.Model;
using Amazon.Lambda.Core;
using BAMCIS.AWSLambda.Common;
using BAMCIS.AWSLambda.Common.CustomResources;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;

// Assembly attribute to enable the Lambda function's JSON input to be converted into a .NET class.
[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.Json.JsonSerializer))]

namespace BAMCIS.LambdaFunctions.ElasticTranscoderPipeline
{
    /// <summary>
    /// The class that will be called by the lambda function.
    /// </summary>
    public class Entrypoint : CustomResourceHandler
    {
        #region Private Fields

        /// <summary>
        /// The Elastic Transcoder client
        /// </summary>
        private IAmazonElasticTranscoder _ETClient = null;

        #endregion

        #region Constructors

        /// <summary>
        /// Default constructor that Lambda will invoke.
        /// </summary>
        public Entrypoint()
        {
            AmazonElasticTranscoderConfig Config = new AmazonElasticTranscoderConfig();
            this._ETClient = new AmazonElasticTranscoderClient(Config);
        }

        #endregion

        #region Public Methods

        public override async Task<CustomResourceResponse> CreateAsync(CustomResourceRequest request, ILambdaContext context)
        {
            try
            {
                context.LogInfo("Attempting to create a pipeline.");
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
                        $"See the details in CloudWatch Log Stream: {context.LogStreamName}.",
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
                context.LogError(e);

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
                context.LogError(e);

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

        public override async Task<CustomResourceResponse> UpdateAsync(CustomResourceRequest request, ILambdaContext context)
        {
            try
            {
                context.LogInfo("Initiating update for pipeline.");

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
                    context.LogWarning($"{Pipelines.Count} pipelines were found matching the Name, InputBucket, and Role specified.");
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
                            $"See the details in CloudWatch Log Stream: {context.LogStreamName}.",
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

        public override async Task<CustomResourceResponse> DeleteAsync(CustomResourceRequest request, ILambdaContext context)
        {
            try
            {
                context.LogInfo("Attempting to delete a pipeline.");

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
                    context.LogWarning($"{Pipelines.Count} pipelines were found matching the Name, InputBucket, and Role specified.");
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
                            $"See the details in CloudWatch Log Stream: {context.LogStreamName}.",
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
                        $"See the details in CloudWatch Log Stream: {context.LogStreamName}.",
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

        #endregion

    }
}
