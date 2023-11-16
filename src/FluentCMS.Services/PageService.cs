﻿using FluentCMS.Entities;
using FluentCMS.Repositories.Abstractions;
using Microsoft.IdentityModel.Tokens;
using Microsoft.VisualBasic;
using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.Security.Policy;

namespace FluentCMS.Services;

public interface IPageService
{
    Task<IEnumerable<Page>> GetBySiteId(Guid siteId, CancellationToken cancellationToken = default);
    Task<Page> GetById(Guid id, CancellationToken cancellationToken = default);
    Task<Page> Create(Page page, CancellationToken cancellationToken = default);
    Task<Page> Update(Page page, CancellationToken cancellationToken = default);
    Task Delete(Guid id, CancellationToken cancellationToken = default);
}

public class PageService : BaseService<Page>, IPageService
{
    private readonly IPageRepository _pageRepository;
    private readonly ISiteRepository _siteRepository;

    public PageService(IApplicationContext applicationContext, IPageRepository pageRepository, ISiteRepository siteRepository) : base(applicationContext)
    {
        _pageRepository = pageRepository;
        _siteRepository = siteRepository;
    }

    public async Task<Page> Create(Page page, CancellationToken cancellationToken = default)
    {
        // Check if site id exists
        var site = (await _siteRepository.GetById(page.SiteId)) ?? throw new AppException(ExceptionCodes.SiteNotFound);

        // check if user is siteAdmin or superAdmin
        if (!Current.IsInRole(site.AdminRoleIds))
        {
            throw new AppPermissionException();
        }

        //Fetch pages beforehand to avoid multiple db calls
        var pages = (await _pageRepository.GetAll(cancellationToken)).ToList();

        //If Parent Id is assigned
        if (page.ParentId != null)
        {
            ValidateParentPage(page, pages, cancellationToken);
        }

        // fetch list of all pages
        ValidateUrl(page, pages);

        // prepare entity for db
        PrepareForCreate(page);

        return await _pageRepository.Create(page, cancellationToken) ?? throw new AppException(ExceptionCodes.PageUnableToCreate);
    }



    private static void ValidateUrl(Page page, List<Page> pages)
    {
        //urls can be cached in a dictionary to avoid multiple list traversal
        var cachedUrls = new Dictionary<Guid, string>();

        // Build Url based on parent
        var fullUrl = BuildFullPath(page, pages, cachedUrls);

        // Check if url is unique
        if (pages.Any(x => BuildFullPath(x, pages, cachedUrls).Equals(fullUrl)))
        {
            throw new AppException(ExceptionCodes.PageUrlNotUnique);
        }
    }

    private static string BuildFullPath(Page page, IEnumerable<Page> pages, Dictionary<Guid, string> cachedUrls)
    {
        //Traverse the pages to root (ParentId == null) and keep them in an array
        var parents = new List<string>
            {
                page.Path
            };
        var parentId = page.ParentId;

        while (parentId != null)
        {
            if (cachedUrls.ContainsKey(parentId.Value))
            {
                parents.Add(cachedUrls[parentId.Value]);
                continue;
            }
            var parent = pages.Single(x => x.Id == parentId);
            parents.Add(parent.Path);
            cachedUrls[parent.Id] = parent.Path;
            parentId = parent.ParentId;
        }
        //Build Path string from List of Parents
        return string.Join("/", parents.Reverse<string>());
    }

    private void ValidateParentPage(Page page, List<Page> pages, CancellationToken cancellationToken)
    {
        var parent = pages.SingleOrDefault(x => x.Id == page.ParentId);
        // If parent id is not a valid page id
        if (parent is null)
        {
            throw new AppException(ExceptionCodes.PageParentPageNotFound);
        }

        // If parent id is not on the same site
        if (parent.SiteId != page.SiteId)
        {
            throw new AppException(ExceptionCodes.PageParentMustBeOnTheSameSite);
        }

        // if page viewRoles are a subset of parent view roles
        if (!page.ViewRoleIds.ToImmutableHashSet().IsSubsetOf(parent.ViewRoleIds))
        {
            throw new AppException(ExceptionCodes.PageViewPermissionsAreNotASubsetOfParent);
        }
    }

    public async Task Delete(Guid id, CancellationToken cancellationToken = default)
    {
        //fetch original page from db
        var originalPage = await _pageRepository.GetById(id, cancellationToken);

        //if page not found
        if (originalPage is null)
        {
            throw new AppException(ExceptionCodes.PageNotFound);
        }

        // fetch site
        var site = (await _siteRepository.GetById(originalPage.SiteId)) ?? throw new AppException(ExceptionCodes.SiteNotFound);

        // check if user is siteAdmin, superAdmin or PageAdmin
        if (!Current.IsInRole(site.AdminRoleIds) && Current.IsInRole(originalPage.AdminRoleIds))
        {
            throw new AppPermissionException();
        }

        // check that it does not have any children
        var pages = (await _pageRepository.GetAll(cancellationToken)).ToList();
        if (pages.Any(x => x.ParentId == id && x.SiteId == originalPage.SiteId))
        {
            throw new AppException(ExceptionCodes.PageHasChildren);
        }

        await _pageRepository.Delete(id, cancellationToken);
    }

    public async Task<Page> GetById(Guid id, CancellationToken cancellationToken = default)
    {
        //fetch page from db
        var page = await _pageRepository.GetById(id, cancellationToken) ?? throw new AppException(ExceptionCodes.PageNotFound);


        // if page view permission is set and current user does not match page permission
        if (!page.ViewRoleIds.IsNullOrEmpty() && !Current.IsInRole(page.ViewRoleIds))
        {
            // fetch site
            var site = (await _siteRepository.GetById(page.SiteId)) ?? throw new AppException(ExceptionCodes.SiteNotFound);

            // check if user is siteAdmin, superAdmin or PageAdmin
            if (!Current.IsInRole(site.AdminRoleIds) && Current.IsInRole(page.AdminRoleIds))
            {
                throw new AppPermissionException();
            }
        }

        return page;
    }

    public async Task<IEnumerable<Page>> GetBySiteId(Guid siteId, CancellationToken cancellationToken = default)
    {
        //fetch site from db
        var site = await _siteRepository.GetById(siteId) ?? throw new AppException(ExceptionCodes.SiteNotFound);

        //fetch pages from db
        var pages = await _pageRepository.GetBySiteId(siteId, cancellationToken);

        //filter pages if page view rols is empty or current user is page viewer or page admin site admin
        return pages.Where(x => x.ViewRoleIds.IsNullOrEmpty() || Current.IsInRole(x.ViewRoleIds) || Current.IsInRole(x.AdminRoleIds) || Current.IsInRole(site.AdminRoleIds));


    }

    public async Task<Page> Update(Page page, CancellationToken cancellationToken = default)
    {
        //fetch original page from db
        var originalPage = await _pageRepository.GetById(page.Id, cancellationToken);

        //if page not found
        if (originalPage is null)
        {
            throw new AppException(ExceptionCodes.PageNotFound);
        }

        // Check if site id exists
        var site = (await _siteRepository.GetById(page.SiteId)) ?? throw new AppException(ExceptionCodes.SiteNotFound);

        // check if user is siteAdmin or superAdmin or pageAdmin
        if (!Current.IsInRole(site.AdminRoleIds) && !Current.IsInRole(originalPage.AdminRoleIds))
        {
            throw new AppPermissionException();
        }

        // site id cannot be changed
        if (originalPage.SiteId != page.SiteId)
        {
            throw new AppException(ExceptionCodes.PageSiteIdCannotBeChanged);
        }

        // fetch list of all pages
        var pages = (await _pageRepository.GetAll(cancellationToken)).ToList();

        //If Parent Id is changed validate again
        if (page.ParentId != originalPage.ParentId)
        {
            //validate parent
            ValidateParentPage(page, pages, cancellationToken);

            //TODO: we should validate children permissions too!
        }

        ValidateUrl(page, pages);

        // prepare entity for db
        PrepareForUpdate(page);

        return await _pageRepository.Update(page, cancellationToken) ?? throw new AppException(ExceptionCodes.PageUnableToUpdate);
    }
}
