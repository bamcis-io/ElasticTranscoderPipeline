# BAMCIS AWS Elastic Transcoder Pipeline

Creates a serverless application that can be used as a custom resource in CloudFormation to create
Elastic Transcoder Pipelines.

## Table of Contents
- [Usage](#usage)
- [Revision History](#revision-history)

## Usage

Deploy the CloudFormation template and Lambda function code. Use the ARN of the lambda function as the `ServiceToken`
property for the custom resource. For example, after this Lambda function is deployed, in the `RESOURCES` section of
you CloudFormation template:

	"Pipeline" : {
		"Type" : "Custom::ElasticTranscoderPipeline",
			"Properties" : {
                "ServiceToken" : {
                    "Ref" : "ElasticTranscoderPipelineArn"
                },
                "Role"         : {
                    "Fn::GetAtt" : [
                        "ElasticTranscoderExecutionRole",
                        "Arn"
                    ]
                },
                "Name"         : {
                    "Ref" : "PipelineName"
                },
                "InputBucket"  : {
                    "Ref" : "VideoInputBucket"
                },
                "Notifications" : {
                    "Error" : {
                        "Fn::GetAtt" : [
                            "SNSTopic",
                            "TopicName"
                        ]
                    }
                },
                "ContentConfig" : {
                    "Bucket" : {
                        "Ref" : "VideoOutBucket"
                    }
                },
                "ThumbnailConfig" : {
                    "Bucket" : {
                        "Ref" : "ThumbnailOutputBucket"
                    }
                }
            }
        },

And this will create an Elastic Transcoder Pipeline with you parameters specified in the `Parameters` section of the CF template.

## Revision History

### 1.0.0
Initial release of the application.
