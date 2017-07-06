using System.Collections.Generic;
using System.Web;
using Sitecore.Configuration;
using Sitecore.Data;
using Sitecore.Data.Items;
using Sitecore.Data.Managers;
using Sitecore.Diagnostics;
using Sitecore.Globalization;
using Sitecore.Layouts;
using Sitecore.SecurityModel;
using Sitecore.Sites;
using Sitecore.Web;

namespace Sitecore.Support.Pipelines.HttpRequest
{
    using System.Linq;
    using Sitecore.Pipelines.HttpRequest;
    using Sitecore.Text;

    /// <summary>
    /// Executes the request.
    /// </summary>
    public class ExecuteRequest : HttpRequestProcessor
    {
        #region Public methods

        /// <summary>
        /// Runs the processor.
        /// </summary>
        /// <param name="args">The arguments.</param>
        public override void Process([NotNull] HttpRequestArgs args)
        {
            Assert.ArgumentNotNull(args, "args");

            SiteContext site = Context.Site;

            if (site != null && !SiteManager.CanEnter(site.Name, Context.User))
            {
                HandleSiteAccessDenied(site, args);
                return;
            }

            PageContext page = Context.Page;

            Assert.IsNotNull(page, "No page context in processor.");

            string filePath = page.FilePath;

            if (filePath.Length > 0)
            {
                if (WebUtil.IsExternalUrl(filePath))
                {
                    args.Context.Response.Redirect(filePath, true);
                    return;
                }

                if (string.Compare(filePath, HttpContext.Current.Request.Url.LocalPath, System.StringComparison.InvariantCultureIgnoreCase) != 0)
                {
                    args.Context.RewritePath(filePath, args.Context.Request.PathInfo, args.Url.QueryString, false);
                }
            }
            else
            {
                if (Context.Item == null)
                {
                    HandleItemNotFound(args);
                }
                else
                {
                    HandleLayoutNotFound(args);
                }
            }
        }

        #endregion

        #region Protected methods

        /// <summary>
        /// Redirects to login page.
        /// </summary>
        /// <param name="url">The URL.</param>
        protected virtual void RedirectToLoginPage(string url)
        {
            var urlString = new UrlString(url);

            if (string.IsNullOrEmpty(urlString["returnUrl"]))
            {
                urlString["returnUrl"] = WebUtil.GetRawUrl();

                urlString.Parameters.Remove("item");
                urlString.Parameters.Remove("user");
                urlString.Parameters.Remove("site");
            }

            WebUtil.Redirect(urlString.ToString(), false);
        }

        /// <summary>
        /// Preforms redirect on 'item not found' condition.
        /// </summary>
        /// <param name="url">The URL.</param>
        protected virtual void RedirectOnItemNotFound(string url)
        {
            PerformRedirect(url);
        }

        /// <summary>
        /// Redirects on 'no access' condition.
        /// </summary>
        /// <param name="url">The URL.</param>
        protected virtual void RedirectOnNoAccess(string url)
        {
            PerformRedirect(url);
        }

        /// <summary>
        /// Redirects the on 'site access denied' condition.
        /// </summary>
        /// <param name="url">The URL.</param>
        protected virtual void RedirectOnSiteAccessDenied(string url)
        {
            PerformRedirect(url);
        }

        /// <summary>
        /// Redirects on 'layout not found' condition.
        /// </summary>
        /// <param name="url">The URL.</param>
        protected virtual void RedirectOnLayoutNotFound(string url)
        {
            PerformRedirect(url);
        }

        /// <summary>
        /// Redirects request to the specified URL.
        /// </summary>
        /// <param name="url">The URL.</param>
        protected virtual void PerformRedirect(string url)
        {
            if (Settings.RequestErrors.UseServerSideRedirect)
            {
                HttpContext.Current.Server.Transfer(url);
            }
            else
            {
                WebUtil.Redirect(url, false);
            }
        }

        #endregion

        #region Private methods

        /// <summary>
        /// Gets the no access URL.
        /// </summary>
        /// <returns>The no access URL.</returns>
        [NotNull]
        string GetNoAccessUrl(out bool loginPage)
        {
            SiteContext site = Context.Site;
            loginPage = false;

            if (site != null && site.LoginPage.Length > 0)
            {
                if (SiteManager.CanEnter(site.Name, Context.User) && !Context.User.IsAuthenticated)
                {
                    Tracer.Info("Redirecting to login page \"" + site.LoginPage + "\".");

                    loginPage = true;
                    return site.LoginPage;
                }

                Tracer.Info("Redirecting to the 'No Access' page as the current user '" + Context.User.Name + "' does not have sufficient rights to enter the '" + site.Name + "' site.");

                return Settings.NoAccessUrl;
            }

            Tracer.Warning("Redirecting to \"No Access\" page as no login page was found.");

            return Settings.NoAccessUrl;
        }

        /// <summary>
        /// Handles the item not found.
        /// </summary>
        /// <param name="args">The arguments.</param>
        void HandleItemNotFound([NotNull] HttpRequestArgs args)
        {
            Debug.ArgumentNotNull(args, "args");

            string itemPath = args.LocalPath;
            string user = Context.User.Name;

            bool noAccess = false;
            bool loginPage = false;
            string url = Settings.ItemNotFoundUrl;

            if (args.PermissionDenied)
            {

                noAccess = true;
                url = GetNoAccessUrl(out loginPage);
            }

            SiteContext site = Context.Site;

            string siteName = (site != null ? site.Name : string.Empty);
            List<string> parameters = new List<string>(new[] { "item", itemPath, "user", user, "site", siteName });
            if (Settings.Authentication.SaveRawUrl)
            {
                parameters.AddRange(new[] { "url", HttpUtility.UrlEncode(Context.RawUrl) });
            }

            url = WebUtil.AddQueryString(url, parameters.ToArray());

            if (!noAccess)
            {
                Log.Warn(string.Format("Request is redirected to document not found page. Requested url: {0}, User: {1}, Website: {2}", Context.RawUrl, user, siteName), this);
                RedirectOnItemNotFound(url);
                return;
            }
            if (loginPage)
            {
                Log.Warn(string.Format("Request is redirected to login page. Requested url: {0}, User: {1}, Website: {2}", Context.RawUrl, user, siteName), this);
                RedirectToLoginPage(url);
            }
            Log.Warn(string.Format("Request is redirected to access denied page. Requested url: {0}, User: {1}, Website: {2}", Context.RawUrl, user, siteName), this);
            RedirectOnNoAccess(url);
        }

        /// <summary>
        /// Handles the layout not found.
        /// </summary>
        /// <param name="args">The arguments.</param>
        void HandleLayoutNotFound([NotNull] HttpRequestArgs args)
        {
            Debug.ArgumentNotNull(args, "args");

            string layout = string.Empty;
            string deviceName = string.Empty;
            string url = string.Empty;
            string message = "Request is redirected to no layout page.";

            DeviceItem device = Context.Device;

            if (device != null)
            {
                deviceName = device.Name;
            }

            Item item = Context.Item;

            if (item != null)
            {
                message += " Item: " + item.Uri;
                if (device != null)
                {
                    message += string.Format(" Device: {0} ({1})", device.ID, device.InnerItem.Paths.Path);
                    layout = item.Visualization.GetLayoutID(device).ToString();
                    if (layout.Length > 0)
                    {
                        Database database = Context.Database;

                        Assert.IsNotNull(database, "No database on processor.");

                        Item layoutItem = ItemManager.GetItem(layout, Language.Current, Version.Latest, database, SecurityCheck.Disable);

                        if (layoutItem != null && !layoutItem.Access.CanRead())
                        {
                            SiteContext site = Context.Site;

                            string siteName = (site != null ? site.Name : string.Empty);

                            url = WebUtil.AddQueryString(Settings.NoAccessUrl, "item", "Layout: " + layout + " (item: " + args.LocalPath + ")", "user", Context.GetUserName(), "site", siteName, "device", deviceName);
                        }
                    }
                }
            }

            if (url.Length == 0)
            {
                url = WebUtil.AddQueryString(Settings.LayoutNotFoundUrl, "item", args.LocalPath, "layout", layout, "device", deviceName);
            }

            Log.Warn(message, this);
            RedirectOnLayoutNotFound(url);
        }

        /// <summary>
        /// Handles 'site access denied'.
        /// </summary>
        void HandleSiteAccessDenied([NotNull] SiteContext site, [NotNull] HttpRequestArgs args)
        {
            Debug.ArgumentNotNull(site, "site");
            Debug.ArgumentNotNull(args, "args");

            string url = Settings.NoAccessUrl;

            url = WebUtil.AddQueryString(url, new[] { "item", args.LocalPath, "user", Context.GetUserName(), "site", site.Name, "right", "site:enter" });

            Log.Warn(string.Format("Request is redirected to access denied page. Requested url: {0}, User: {1}, Website: {2}", Context.RawUrl, Context.GetUserName(), site.Name), this);
            RedirectOnSiteAccessDenied(url);
        }

        #endregion
    }
}