﻿namespace FluentCMS.Services;

public interface IContentTypeService : IAutoRegisterService
{
    Task<IEnumerable<ContentType>> GetAll(Guid siteId, CancellationToken cancellationToken = default);
    Task<ContentType> GetBySlug(Guid siteId, string contentTypeSlug, CancellationToken cancellationToken = default);
    Task<ContentType> Create(ContentType contentType, CancellationToken cancellationToken = default);
    Task<ContentType> Update(ContentType contentType, CancellationToken cancellationToken = default);
    Task<ContentType> Delete(Guid contentTypeId, CancellationToken cancellationToken = default);
    Task<ContentType> SetField(Guid contentTypeId, ContentTypeField field, CancellationToken cancellationToken = default);
    Task<ContentType> DeleteField(Guid contentTypeId, string name, CancellationToken cancellationToken = default);
    Task<ContentType> GetById(Guid id, CancellationToken cancellationToken);
}

public class ContentTypeService(IContentTypeRepository contentTypeRepository, IMessagePublisher messagePublisher) : IContentTypeService
{
    public Task<IEnumerable<ContentType>> GetAll(Guid siteId, CancellationToken cancellationToken = default)
    {
        return contentTypeRepository.GetAllForSite(siteId, cancellationToken);
    }

    public async Task<ContentType> GetBySlug(Guid siteId, string contentTypeSlug, CancellationToken cancellationToken = default)
    {
        return await contentTypeRepository.GetBySlug(siteId, contentTypeSlug, cancellationToken) ??
            throw new AppException(ExceptionCodes.ContentTypeNotFound);
    }

    public async Task<ContentType> Create(ContentType contentType, CancellationToken cancellationToken = default)
    {
        await CheckDuplicateSlug(contentType.SiteId, contentType);

        var created = await contentTypeRepository.Create(contentType, cancellationToken) ??
            throw new AppException(ExceptionCodes.ContentTypeUnableToCreate);

        await messagePublisher.Publish(new Message<ContentType>(ActionNames.ContentTypeCreated, created), cancellationToken);

        return created;
    }

    public async Task<ContentType> Delete(Guid contentTypeId, CancellationToken cancellationToken = default)
    {
        var deleted = await contentTypeRepository.Delete(contentTypeId, cancellationToken) ??
            throw new AppException(ExceptionCodes.ContentTypeUnableToDelete);

        await messagePublisher.Publish(new Message<ContentType>(ActionNames.ContentTypeDeleted, deleted), cancellationToken);

        return deleted;
    }

    public async Task<ContentType> Update(ContentType contentType, CancellationToken cancellationToken = default)
    {
        // only allow name and description to be updated
        var original = await contentTypeRepository.GetById(contentType.Id, cancellationToken) ??
            throw new AppException(ExceptionCodes.ContentTypeNotFound);

        await CheckDuplicateSlug(original.SiteId, contentType);

        original.Title = contentType.Title;
        original.Description = contentType.Description;

        var updated = await contentTypeRepository.Update(original, cancellationToken) ??
            throw new AppException(ExceptionCodes.ContentTypeUnableToUpdate);

        await messagePublisher.Publish(new Message<ContentType>(ActionNames.ContentTypeUpdated, updated), cancellationToken);

        return updated;
    }

    private async Task CheckDuplicateSlug(Guid siteId, ContentType contentType)
    {
        var originalBySlug = await contentTypeRepository.GetBySlug(siteId, contentType.Slug);
        if (originalBySlug != null && originalBySlug.Id != contentType.Id) throw new AppException(ExceptionCodes.ContentTypeDuplicateSlug);
    }

    public async Task<ContentType> SetField(Guid contentTypeId, ContentTypeField field, CancellationToken cancellationToken = default)
    {
        // load the content type
        var contentType = await contentTypeRepository.GetById(contentTypeId, cancellationToken) ??
            throw new AppException(ExceptionCodes.ContentTypeNotFound);

        // check the field exists
        var originalField = contentType.Fields.FirstOrDefault(f => f.Name == field.Name);

        if (originalField == null)
            contentType.Fields.Add(field);
        else
        {
            originalField.Type = field.Type;
            originalField.Settings = field.Settings;
            originalField.Name = field.Name;
            originalField.Required = field.Required;
            originalField.Unique = field.Unique;
            originalField.Label = field.Label;
        }

        var updated = await contentTypeRepository.Update(contentType, cancellationToken) ??
            throw new AppException(ExceptionCodes.ContentTypeUnableToUpdate);

        await messagePublisher.Publish(new Message<ContentType>(ActionNames.ContentTypeUpdated, updated), cancellationToken);

        return updated;
    }

    public async Task<ContentType> DeleteField(Guid contentTypeId, string name, CancellationToken cancellationToken = default)
    {
        // load the content type
        var contentType = await contentTypeRepository.GetById(contentTypeId, cancellationToken) ??
            throw new AppException(ExceptionCodes.ContentTypeNotFound);

        // check the field exists
        var original = contentType.Fields.FirstOrDefault(f => f.Name == name) ??
            throw new AppException(ExceptionCodes.ContentTypeFieldNotFound);

        // remove the field
        contentType.Fields.Remove(original);

        //apply changes
        var updated = await contentTypeRepository.Update(contentType, cancellationToken) ??
            throw new AppException(ExceptionCodes.ContentTypeUnableToUpdate);

        await messagePublisher.Publish(new Message<ContentType>(ActionNames.ContentTypeUpdated, updated), cancellationToken);

        return updated;
    }

    public async Task<ContentType> GetById(Guid id, CancellationToken cancellationToken)
    {
        return await contentTypeRepository.GetById(id, cancellationToken) ??
               throw new AppException(ExceptionCodes.ContentTypeNotFound);
    }

}
