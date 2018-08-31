/*
 * 
 * 08.30.2018 For now, just grab title and price
 * 
 * 
 */

using HtmlAgilityPack;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using wallib.Models;

namespace wal
{
    class Program
    {
        private static string Log_File = "log.txt";
        static walDB db = new walDB();

        static void Main(string[] args)
        {
            var items = new List<WalItem>();

            if (args == null || args.Count() == 0)
            {
                Console.WriteLine("please provide a category");
            }
            else
            {
                int categoryId = Convert.ToInt32(args[0]);

                // action toys, retailer=walmart.com, >= $35
                // list view
                string url = "https://www.walmart.com/browse/toys/action-figures/4171_4172/?cat_id=4171_4172&facet=retailer%3AWalmart.com&grid=false&max_price=&min_price=35&page={0}#searchProductResult";
                Task.Run(async () =>
                {
                    items = await GetItems(url, categoryId);
                    db.RemoveItemRecords(categoryId);
                    await StoreItems(items);
                }).Wait();

                Console.WriteLine("complete");
                Console.ReadKey();
            }
        }

        public static async Task<List<WalItem>> GetItems(string url, int categoryId)
        {
            bool done = false;
            int pageNum = 0;
            string page = null;
            int titleCount = 0;
            var allitems = new List<WalItem>();
            var items = new List<WalItem>();

            // If you page passed the "end" of the results, walmart tells you this and keeps displaying other results.
            // The following text tells you this.
            string endMarker = "find any results with the Price Filter you selected. Showing results without";

            try
            {
                while (!done)
                {
                    ++pageNum;
                    page = string.Format(url, pageNum);

                    using (HttpClient client = new HttpClient())
                    using (HttpResponseMessage response = await client.GetAsync(page))
                    using (HttpContent content = response.Content)
                    {
                        // ... Read the string.
                        string result = await content.ReadAsStringAsync();
                        int pos = result.IndexOf(endMarker);
                        if (pos > -1)
                            done = true;
                        else
                        {
                            items = await ProcessTitles(result, pageNum, categoryId);
                            allitems.AddRange(items);
                        }
                    }
                }
                Console.WriteLine("found: " + titleCount);

            }
            catch (Exception exc)
            {
                string err = exc.Message;
            }
            return allitems;
        }

        public static async Task<WalItem> GetDetail(string url)
        {
            var item = new WalItem();
            string marker = "Walmart #";
            string itemNo = null;

            try
            {
                using (HttpClient client = new HttpClient())
                using (HttpResponseMessage response = await client.GetAsync(url))
                using (HttpContent content = response.Content)
                {
                    // ... Read the string.
                    string result = await content.ReadAsStringAsync();
                    itemNo = parseItemNo(result);
                    item.ItemId = itemNo;
                }
            }
            catch (Exception exc)
            {
                string err = exc.Message;
            }
            return item;
        }

        protected static string parseItemNo(string html)
        {
            const string marker = "Walmart #";
            const int maxlen = 11;
            string itemNo = null;
            bool done = false;
            int pos = 0;
            int endPos = 0;

            pos = html.IndexOf(marker);
            do
            {
                if (pos > -1)
                {
                    endPos = html.IndexOf("<", pos);
                    if (endPos > -1)
                    {
                        itemNo = html.Substring(pos + marker.Length, endPos - (pos + marker.Length));
                        itemNo = itemNo.Trim();
                        if (itemNo.Length < maxlen)
                            done = true;
                        else
                        {
                            pos = html.IndexOf(marker, endPos);
                            if (pos == -1)
                                done = true;
                        }
                    }
                }
                else
                    done = true;
            }
            while (!done);

            return itemNo;
        }

        // helpful to debugging to log page number
        public static async Task<List<WalItem>> ProcessTitles(string html, int pageNum, int categoryId)
        {
            bool done = false;
            int pos = 0;
            int startTitle = 0;
            int titleLen = 0;
            int htmlLen = html.Length;
            int titleCount = 0;
            var items = new List<WalItem>();

            const string titleMarker = "\"productType\":\"REGULAR\",\"title\":";
            string productPageUrl = null;
            string offerPrice = null;

            const string whereToStart = "Walmart mobile apps";
            pos = html.IndexOf(whereToStart);

            try
            {
                do
                {
                    pos = html.IndexOf(titleMarker, pos);
                    if (pos == -1)
                        done = true;
                    else
                    {
                        int endPos = html.IndexOf("\",\"", pos + titleMarker.Length + 1);

                        startTitle = pos + titleMarker.Length + 1;
                        titleLen = endPos - pos - titleMarker.Length - 1;
                        string title = html.Substring(startTitle, titleLen);
                        Console.WriteLine(title);
                        dsutil.DSUtil.WriteFile(Log_File, title);

                        productPageUrl = "https://www.walmart.com" + getProductPageUrl(html, endPos);
                        dsutil.DSUtil.WriteFile(Log_File, productPageUrl);

                        offerPrice = getOfferPrice(html, endPos);
                        dsutil.DSUtil.WriteFile(Log_File, "offer price: " + offerPrice);

                        dsutil.DSUtil.WriteFile(Log_File, "page num: " + pageNum);
                        dsutil.DSUtil.WriteFile(Log_File, "", blankLine: true);

                        var item = new WalItem();
                        item.DetailUrl = productPageUrl;
                        item.Title = title;
                        item.CategoryID = categoryId;
                        item.Price = Convert.ToDecimal(offerPrice);

                        var detail = await GetDetail(item.DetailUrl);
                        item.ItemId = detail.ItemId;

                        items.Add(item);

                        ++titleCount;
                        pos += titleMarker.Length;
                    }
                } while (!done);
            }
            catch (Exception exc)
            {
                string err = exc.Message;
            }
            return items;
        }

        // get detail link
        protected static string getProductPageUrl(string html, int startSearching)
        {
            const string productPageMarker = "productPageUrl\":\"";
            int endProductPagePos = 0;
            string productPageUrl = null;

            int productPagePos = html.IndexOf(productPageMarker, startSearching);
            endProductPagePos = html.IndexOf(",", productPagePos + 1);
            productPageUrl = html.Substring(productPagePos + productPageMarker.Length, endProductPagePos - (productPagePos + productPageMarker.Length) - 1);
            return productPageUrl;
        }

        protected static string getOfferPrice(string html, int startSearching)
        {
            const string priceMarker = "offerPrice\":";
            int endPricePos = 0;
            string offerPrice = null;

            int pricePos = html.IndexOf(priceMarker, startSearching);
            endPricePos = html.IndexOf(",", pricePos + 1);
            offerPrice = html.Substring(pricePos + priceMarker.Length, endPricePos - (pricePos + priceMarker.Length));
            return offerPrice;
        }

        protected static async Task StoreItems(List<WalItem> items)
        {
            int count = items.Count();
            int i = 0;
            foreach(WalItem item in items)
            {
                Console.WriteLine("store item " + (++i) + " of " + count);
                if (item.Title.Length > 200)
                {
                    int a = 100;
                }
                if (item.DetailUrl.Length > 300)
                {
                    int b = 100;
                }
                if (!string.IsNullOrEmpty(item.ItemId))
                {
                    if (item.ItemId.Length > 20)
                    {
                        int c = 100;
                    }
                }
                await db.ItemStore(item);
            }
        }
    }
}
