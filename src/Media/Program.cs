using MassTransit;
using MassTransit.Caching.Internals;
using Media.Infrastructure;
using Media.Infrastructure.IntegrationEvents;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Minio;
using Minio.DataModel.Args;

var builder = WebApplication.CreateBuilder(args);




builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();


builder.Services.AddDbContext<MediaDbContext>(configure => configure.UseInMemoryDatabase("MediaDb"));

builder.BrokerConfigure();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();



app.MapPost("/{bucket_name}/{catalog_id}", async (
    [FromRoute(Name = "bucket_name")] string bucketName,
    [FromRoute(Name = "catalog_id")] string catalogId,
    IFormFile formFile,
    MediaDbContext dbContext,
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

        var token = new UrlToken()
        {
            Id = Guid.NewGuid(),
            BucketName = bucketName,
            ObjectName = formFile.Name,
            ContentType=formFile.ContentType,
            ExpireOn=DateTime.UtcNow.AddMinutes(10),
        };

        dbContext.UrlTokens.Add(token);
        await dbContext.SaveChangesAsync();

        string url = $"http://localhost:5149/{token.Id}";
        await publisher.Publish(new MediaUploadedEvent(formFile.FileName, url, catalogId, DateTime.UtcNow));
    }
    catch (Exception)
    {

        throw;
    }

}).DisableAntiforgery();




app.MapGet("/{token:guid:required}", async (
      MediaDbContext dbContext,
       IConfiguration configuration,
       Guid Token) => 
{


    var foundToken = await dbContext.UrlTokens.FirstOrDefaultAsync(x => x.Id == Token&&x.ExpireOn<=DateTime.UtcNow);

    if (foundToken is null)
        throw new InvalidOperationException();

    foundToken.CountAccess++;

    var endpoint = configuration["MinioStorage:MinioEndpoint"];
    var accessKey = configuration["MinioStorage:AccessKey"];
    var secretKey = configuration["MinioStorage:SecretKey"];

    var minio = new MinioClient()
                         .WithEndpoint(endpoint)
                         .WithCredentials(accessKey, secretKey)
                         .Build();

    var memoryStream = new MemoryStream();
    GetObjectArgs getObjectArgs = new GetObjectArgs()
                                      .WithBucket(foundToken.BucketName)
                                      .WithObject(foundToken.ObjectName)
                                      .WithCallbackStream((stream) =>
                                      {
                                          stream.CopyTo(memoryStream);
                                      });

    await minio.GetObjectAsync(getObjectArgs);


    return Results.File(memoryStream.ToArray(), contentType: foundToken.ContentType);

});



app.Run();

