using MassTransit;
using Media.Infrastructure;
using Media.Infrastructure.IntegrationEvents;
using Microsoft.AspNetCore.Mvc;
using Minio;
using Minio.DataModel.Args;

var builder = WebApplication.CreateBuilder(args);




builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.BrokerConfigure();


var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();



app.MapPost("/{bucket_name}/{catalog_id}", async(
    [FromRoute(Name ="bucket_name")] string bucketName,
    [FromRoute(Name ="catalog_id")] string catalogId,
    IFormFile formFile, 
    IPublishEndpoint publisher, 
    IConfiguration configuration) =>
{

    var endpoint = configuration["MinioStorage:MinioEndpoint"];
    var accessKey = configuration["MinioStorage:AccessKey"];
    var secretKey = configuration["MinioStorage:SecretKey"];

    var minio = new MinioClient()
                         .WithEndpoint(endpoint)
                         .WithCredentials(accessKey, secretKey)
                         .Build();


    var putObjectOrgs = new PutObjectArgs()
                          .WithBucket(bucketName)
                          .WithObject(formFile.Name)
                          .WithContentType(formFile.ContentType)
                          .WithStreamData(formFile.OpenReadStream())
                          .WithObjectSize(formFile.Length);


    try
    {
        await minio.PutObjectAsync(putObjectOrgs);
        string url = $"{endpoint}/{bucketName}/{formFile.Name}";
        await publisher.Publish(new MediaUploadedEvent(formFile.FileName, url, catalogId, DateTime.UtcNow));
    }
    catch (Exception)
    {

        throw;
    }

}).DisableAntiforgery();


app.Run();

