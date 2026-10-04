"""Prepare private deployment storage in the explicitly selected demo account."""
import boto3
from botocore.exceptions import ClientError

ACCOUNT = '590183968738'
BUCKET = 'guategeeks-aws2026-artifacts-590183968738-us-east-1'
session = boto3.Session(profile_name='awsday', region_name='us-east-1')
if session.client('sts').get_caller_identity()['Account'] != ACCOUNT:
    raise RuntimeError('Unexpected account; stopping')
s3 = session.client('s3')
try:
    s3.head_bucket(Bucket=BUCKET, ExpectedBucketOwner=ACCOUNT)
    print('Reusing owned artifact bucket:', BUCKET)
except ClientError as error:
    if error.response['ResponseMetadata']['HTTPStatusCode'] != 404:
        raise
    s3.create_bucket(Bucket=BUCKET)
    s3.put_public_access_block(Bucket=BUCKET, ExpectedBucketOwner=ACCOUNT,
        PublicAccessBlockConfiguration=dict(BlockPublicAcls=True, IgnorePublicAcls=True, BlockPublicPolicy=True, RestrictPublicBuckets=True))
    s3.put_bucket_encryption(Bucket=BUCKET, ExpectedBucketOwner=ACCOUNT,
        ServerSideEncryptionConfiguration={'Rules': [{'ApplyServerSideEncryptionByDefault': {'SSEAlgorithm': 'AES256'}}]})
    s3.put_bucket_ownership_controls(Bucket=BUCKET, ExpectedBucketOwner=ACCOUNT,
        OwnershipControls={'Rules': [{'ObjectOwnership': 'BucketOwnerEnforced'}]})
    s3.put_bucket_tagging(Bucket=BUCKET, ExpectedBucketOwner=ACCOUNT,
        Tagging={'TagSet': [{'Key': 'Project', 'Value': 'GuateGeeksAWS2026'}, {'Key': 'Purpose', 'Value': 'Backend deployment artifacts'}]})
    print('Created private, encrypted artifact bucket:', BUCKET)
