using System.Collections.Generic;
using Microsoft.AspNetCore.Mvc.Rendering;
using Nop.Web.Framework.Mvc.ModelBinding;
using Nop.Web.Framework.Mvc.Models;

namespace Nop.Web.Areas.Admin.Models.Plugins
{
    public partial class OfficialFeedListModel : BaseNopModel
    {
        public OfficialFeedListModel()
        {
            AvailableVersions = new List<SelectListItem>();
            AvailableCategories = new List<SelectListItem>();
            AvailablePrices = new List<SelectListItem>();
        }

        [NopResourceDisplayName("Admin.Configuration.Plugins.OfficialFeed.Name")]
        public string SearchName { get; set; }
        [NopResourceDisplayName("Admin.Configuration.Plugins.OfficialFeed.Version")]
        public int SearchVersionId { get; set; }
        [NopResourceDisplayName("Admin.Configuration.Plugins.OfficialFeed.Category")]
        public int SearchCategoryId { get; set; }
        [NopResourceDisplayName("Admin.Configuration.Plugins.OfficialFeed.Price")]
        public int SearchPriceId { get; set; }

        [NopResourceDisplayName("Admin.Configuration.Plugins.OfficialFeed.Version")]
        public IList<SelectListItem> AvailableVersions { get; set; }
        [NopResourceDisplayName("Admin.Configuration.Plugins.OfficialFeed.Category")]
        public IList<SelectListItem> AvailableCategories { get; set; }
        [NopResourceDisplayName("Admin.Configuration.Plugins.OfficialFeed.Price")]
        public IList<SelectListItem> AvailablePrices { get; set; }

        #region Nested classes

        public partial class ItemOverview
        {
            public string Url { get; set; }
            public string Name { get; set; }
            public string CategoryName { get; set; }
            public string SupportedVersions { get; set; }
            public string PictureUrl { get; set; }
            public string Price { get; set; }
        }

        #endregion
    }
}