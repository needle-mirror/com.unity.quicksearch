﻿// #define QUICKSEARCH_DEBUG
using System.Collections.Generic;
using UnityEngine;
using System;
using UnityEngine.Networking;
using System.Text;
using JetBrains.Annotations;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using UnityEditor;
using Debug = UnityEngine.Debug;

namespace Unity.QuickSearch.Providers
{
    static class AssetStoreProvider
    {
        #pragma warning disable CS0649
        [Serializable]
        class Request
        {
            public string q;
        }

        [Serializable]
        class ResponseHeader
        {
            public int status;
            public int QTime;
        }

        [UsedImplicitly, Serializable]
        class ErrorObject
        {
            public string msg;
        }

        [UsedImplicitly, Serializable]
        class ResponseObject
        {
            public int numFound;
            public int numInserted;
            public int start;
            public AssetDocument[] docs;
        }

        [UsedImplicitly, Serializable]
        class AssetDocument
        {
            public string id;
            public string name_en_US;
            public float price_USD;
            public string publisher;
            public string[] icon;
            public string url;
            public string category_slug;
            // public string type;
            // public string name_ja-JP;
            // public string name_ko-KR;
            // public string name_zh-CN;
            // public int avg_rating;
            // public int ratings;
            // public int publisher_id;
            // public string on_sale;
            // public string plus_pro;
            public string[] key_images;
            // public float original_price_USD;
            // public float original_price_EUR;
            // public int age;
            // public string new;
            // public string partner;

            // Cache for the PurchaseInfo API
            public PurchaseDetail purchaseDetail;
        }

        [UsedImplicitly, Serializable]
        class StoreResponse
        {
            public ResponseHeader responseHeader;
            public ResponseObject response;
            public ErrorObject error;
        }

        [UsedImplicitly, Serializable]
        class AccessToken
        {
            public string access_token;
            public string expires_in;

            public long expiration;
            public double expirationStarts;

            // public string token_type;
            // public string refresh_token;
            // public string user;
            // public string display_name;
        }

        [UsedImplicitly, Serializable]
        class TokenInfo
        {
            public string sub;
            public string access_token;
            public string expires_in;

            public long expiration;
            public double expirationStarts;
            // public string scopes;
            // public string client_id;
            // public string ip_address;

        }

        [UsedImplicitly, Serializable]
        class UserInfoName
        {
            public string fillName;
        }

        [UsedImplicitly, Serializable]
        class UserInfo
        {
            public string id;
            public string username;
            public UserInfoName name;
        }

        [UsedImplicitly, Serializable]
        class PurchaseInfo
        {
            public int packageId;
        }

        [UsedImplicitly, Serializable]
        class PurchaseResponse
        {
            public int total;
            public PurchaseInfo[] results;
        }

        [UsedImplicitly, Serializable]
        class PurchaseDetailCategory
        {
            public string id;
            public string name;
            public string slug;
        }

        [UsedImplicitly, Serializable]
        class PurchaseDetailMainImage
        {
            public string big;
            public string icon;
            public string icon25;
            public string icon75;
            public string small;
            public string url;
            public string facebook;
        }

        [UsedImplicitly, Serializable]
        class PurchaseDetail
        {
            public string packageId;
            public string ownerId;
            public string name;
            public string displayName;
            public string publisherId;
            public PurchaseDetailCategory category;
            public PurchaseDetailMainImage mainImage;
        }
        #pragma warning restore CS0649

        class PreviewData
        {
            public Texture2D preview;
            public UnityWebRequest request;
            public UnityWebRequestAsyncOperation requestOp;
        }

        enum QueryParamType
        {
            kString,
            kFloat,
            kBoolean,
            kStringArray,
            kInteger
        }

        class QueryParam
        {
            public QueryParamType type;
            public string name;
            public string keyword;
            public string queryName;

            public QueryParam(QueryParamType type, string name, string queryName = null)
            {
                this.type = type;
                this.name = name;
                keyword = name + ":";
                this.queryName = queryName ?? name;
            }
        }

        private const string kSearchEndPoint = "https://assetstore.unity.com/api/search";
        private static Dictionary<string, PreviewData> s_Previews = new Dictionary<string, PreviewData>();
        private static bool s_StartPurchaseRequest;
        private static List<PurchaseInfo> s_Purchases = new List<PurchaseInfo>();
        internal static HashSet<string> purchasePackageIds;
        #if UNITY_2019_3_OR_NEWER
        private static string s_PackagesKey;
        #endif
        private static string s_AuthCode;
        private static AccessToken s_AccessTokenData;
        private static TokenInfo s_TokenInfo;
        private static UserInfo s_UserInfo;
        #if UNITY_2020_1_OR_NEWER
        private static Action<string> s_OpenPackageManager;
        #endif

        private static readonly List<QueryParam> k_QueryParams = new List<QueryParam>
        {
            new QueryParam(QueryParamType.kFloat, "min_price"),
            new QueryParam(QueryParamType.kFloat, "max_price"),
            new QueryParam(QueryParamType.kStringArray, "publisher"),
            new QueryParam(QueryParamType.kString, "version", "unity_version"),
            new QueryParam(QueryParamType.kBoolean, "free"),
            new QueryParam(QueryParamType.kBoolean, "on_sale"),
            new QueryParam(QueryParamType.kBoolean, "plus_pro_sale"),
            new QueryParam(QueryParamType.kStringArray, "category"),
            new QueryParam(QueryParamType.kInteger, "min_rating"),
            new QueryParam(QueryParamType.kInteger, "sort"),
            new QueryParam(QueryParamType.kBoolean, "reverse"),
            new QueryParam(QueryParamType.kInteger, "start"),
            new QueryParam(QueryParamType.kInteger, "rows"),
            new QueryParam(QueryParamType.kString, "lang"),
        };

        private static IEnumerable<SearchItem> SearchStore(SearchContext context, SearchProvider provider)
        {
            if (string.IsNullOrEmpty(context.searchQuery))
                yield break;

            var requestQuery = new Dictionary<string, object>()
            {
                { "q", context.searchQuery }
            };
            ProcessFilter(context, requestQuery);

            var requestStr = Utils.JsonSerialize(requestQuery);
            var webRequest = Post(kSearchEndPoint, requestStr);
            var rao = webRequest.SendWebRequest();
            while (!rao.isDone)
                yield return null;

            StoreResponse response;
            // using (new DebugTimer("Parse response"))
            {
                var saneJsonStr = webRequest.downloadHandler.text.Replace("name_en-US\"", "name_en_US\"");
                response = JsonUtility.FromJson<StoreResponse>(saneJsonStr);
            }

            if (!string.IsNullOrEmpty(webRequest.error))
            {
                Debug.Log($"Asset store request error: {webRequest.error}");
            }
            else
            {
                if (response.responseHeader.status != 0)
                {
                    if (response.error != null)
                        Debug.LogError($"Error: {response.error.msg}");
                }
                else
                {
                    var scoreIndex = 1;
                    foreach (var doc in response.response.docs)
                    {
                        yield return CreateItem(context, provider, doc, scoreIndex++);
                    }
                }
            }
        }

        static void ProcessFilter(SearchContext context, Dictionary<string, object> request)
        {
            foreach (var filter in context.textFilters)
            {
                var filterTokens = filter.Split(':');
                var qp = k_QueryParams.Find(p => p.name == filterTokens[0]);
                if (qp == null || (filterTokens.Length != 2 && qp.type != QueryParamType.kBoolean))
                    continue;
                switch (qp.type)
                {
                    case QueryParamType.kBoolean:
                        {
                            var isOn = false;
                            if (filterTokens.Length == 2)
                            {
                                isOn = TryConvert.ToBool(filterTokens[1]);
                            }
                            else
                            {
                                isOn = true;
                            }
                            request.Add(qp.queryName, isOn);
                            break;
                        }
                    case QueryParamType.kInteger:
                        {
                            var value = TryConvert.ToInt(filterTokens[1]);
                            request.Add(qp.queryName, value);
                            break;
                        }
                    case QueryParamType.kFloat:
                        {
                            var value = TryConvert.ToFloat(filterTokens[1]);
                            request.Add(qp.queryName, value);
                            break;
                        }
                    case QueryParamType.kString:
                        {
                            request.Add(qp.queryName, filterTokens[1]);
                            break;
                        }
                    case QueryParamType.kStringArray:
                        {
                            request.Add(qp.queryName, new object[] { filterTokens[1] });
                            break;
                        }
                }

            }
        }

        static SearchItem CreateItem(SearchContext context, SearchProvider provider, AssetDocument doc, int score)
        {
            var priceStr = "";
            if (purchasePackageIds!= null && purchasePackageIds.Contains(doc.id))
            {
                priceStr = "Owned";
            }
            else
            {
                priceStr = doc.price_USD == 0 ? "Free" : $"{doc.price_USD}$";
            }
            
            var item = provider.CreateItem(doc.id, score, doc.name_en_US, $"{doc.publisher} - {doc.category_slug} - <color=#F6B93F>{priceStr}</color>", null, doc);
            doc.purchaseDetail = null;
            doc.url = $"https://assetstore.unity.com/packages/{doc.category_slug}/{doc.id}";
            return item;
        }

        static UnityWebRequest Post(string url, string jsonData)
        {
            var request = new UnityWebRequest(url, "POST");
            byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonData);
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            return request;
        }

        static void OnEnable()
        {
            CheckPurchases();
        }

        static void CheckPurchases()
        {
            #if UNITY_2019_3_OR_NEWER
            if (UnityEditorInternal.InternalEditorUtility.inBatchMode)
                return;

            if (s_PackagesKey == null)
                s_PackagesKey = GetPackagesKey();

            if (s_StartPurchaseRequest)
                return;

            s_StartPurchaseRequest = true;
            var startRequest = System.Diagnostics.Stopwatch.StartNew();
            GetAllPurchases((purchases, error) =>
            {
                s_StartPurchaseRequest = false;
                if (error != null)
                {
                    Debug.LogError($"Error in fetching user purchases: {error}");
                    return;
                }
                startRequest.Stop();
                // Debug.Log($"Fetch purchases in {startRequest.ElapsedMilliseconds}ms");

                purchasePackageIds = new HashSet<string>();
                foreach (var purchaseInfo in purchases)
                {
                    purchasePackageIds.Add(purchaseInfo.packageId.ToString());
                }
            });
            #endif
        }

        [UsedImplicitly, SearchItemProvider]
        internal static SearchProvider CreateProvider()
        {
            return new SearchProvider("store", "Asset Store")
            {
                #if UNITY_2020_1_OR_NEWER
                active = true,
                #else
                active = false,
                #endif
                filterId = "store:",
                onEnable = OnEnable,
                showDetails = true,
                fetchItems = (context, items, provider) => SearchStore(context, provider),
                fetchThumbnail = (item, context) => FetchImage(((AssetDocument)item.data).icon, false, s_Previews),
                fetchPreview = (item, context, size, options) => 
                {
                    if (!options.HasFlag(FetchPreviewOptions.Large))
                        return null;

                    var doc = (AssetDocument)item.data;
                    if (doc.purchaseDetail == null)
                    {
                        var productId = Convert.ToInt32(doc.id);
                        GetPurchaseInfo(Convert.ToInt32(doc.id), (detail, error) =>
                        {
                            if (error != null)
                            {
                                return;
                            }

                            doc.purchaseDetail = detail;
                        });
                        return null;
                    }

                    if (doc.purchaseDetail?.mainImage?.big != null)
                        return FetchImage(new[] { doc.purchaseDetail.mainImage.big }, false, s_Previews);

                    if (doc.key_images.Length > 0)
                        return FetchImage(doc.key_images, true, s_Previews);

                    return FetchImage(doc.icon, true, s_Previews);
                },
                fetchKeywords = (context, lastToken, items) =>
                {
                    items.AddRange(k_QueryParams.Select(QueryParam => QueryParam.keyword));
                }
            };
        }

        static Texture2D FetchImage(string[] imageUrls, bool animateCarrousel, Dictionary<string, PreviewData> imageDb)
        {
            if (imageUrls == null || imageUrls.Length == 0)
                return null;

            var keyImage = imageUrls[0];
            if (animateCarrousel)
            {
                var imageIndex = Mathf.FloorToInt(Mathf.Repeat((float)UnityEditor.EditorApplication.timeSinceStartup, imageUrls.Length));
                keyImage = imageUrls[imageIndex];
            }

            if (imageDb.TryGetValue(keyImage, out var previewData))
            {
                if (previewData.preview)
                    return previewData.preview;
                return null;
            }

            var newPreview = new PreviewData { request = UnityWebRequestTexture.GetTexture(keyImage) };
            newPreview.requestOp = newPreview.request.SendWebRequest();
            newPreview.requestOp.completed += (aop) =>
            {
                if (newPreview.request.isDone && !newPreview.request.isHttpError && !newPreview.request.isNetworkError)
                    newPreview.preview = DownloadHandlerTexture.GetContent(newPreview.request);
                newPreview.requestOp = null;
            };
            imageDb[keyImage] = newPreview;
            return newPreview.preview;
        }

        [UsedImplicitly, SearchActionsProvider]
        internal static IEnumerable<SearchAction> ActionHandlers()
        {
            return new[]
            {
                #if UNITY_2020_1_OR_NEWER
                new SearchAction("store", "open", null, "Open item")
                {
                    handler = (item, context) =>
                    {
                        var doc = (AssetDocument)item.data;
                        if (AssetStoreProvider.purchasePackageIds != null && AssetStoreProvider.purchasePackageIds.Contains(doc.id))
                        {
                            OpenPackageManager(doc.name_en_US);
                        }
                        else
                        {
                            BrowseAssetStoreItem(item);
                        }
                    }
                },
                #endif
                new SearchAction("store", "browse", null, "Browse item")
                {
                    handler = (item, context) =>
                    {
                        BrowseAssetStoreItem(item);
                    }
                }
            };
        }

        static void BrowseAssetStoreItem(SearchItem item)
        {
            var doc = (AssetDocument)item.data;
            SearchUtility.Goto(doc.url);
            CheckPurchases();
        }

        static string GetPackagesKey()
        {
            // using (new DebugTimer("GetPackagesKey"))
            {
                // We want to do this:
                // UnityEditor.Connect.UnityConnect.instance.GetConfigurationURL(CloudConfigUrl.CloudPackagesKey);
                var assembly = typeof(UnityEditor.Connect.UnityOAuth).Assembly;
                var managerType = assembly.GetTypes().First(t => t.Name == "UnityConnect");
                var instanceAccessor = managerType.GetProperty("instance", BindingFlags.Public | BindingFlags.Static);
                var instance = instanceAccessor.GetValue(null);
                var getConfigUrl = managerType.GetMethod("GetConfigurationURL");
                var cloudConfigUrlEnum = assembly.GetTypes().First(t => t.Name == "CloudConfigUrl");
                var packmanKey = cloudConfigUrlEnum.GetEnumValues().GetValue(12);
                var packageKey = (string)getConfigUrl.Invoke(instance, new[] { packmanKey });
                return packageKey;
            }
        }

        static void OpenPackageManager(string packageName)
        {
            #if UNITY_2020_1_OR_NEWER
            if (s_OpenPackageManager == null)
            {
                // We want to do this:
                // UnityEditor.PackageManager.UI.PackageManagerWindow.SelectPackageAndFilter

                var assembly = typeof(UnityEditor.PackageManager.UI.Window).Assembly;
                var managerType = assembly.GetTypes().First(t => t.Name == "PackageManagerWindow");
                var methodInfo = managerType.GetMethod("SelectPackageAndFilter", BindingFlags.Static | BindingFlags.NonPublic);
                var cloudConfigUrlEnum = assembly.GetTypes().First(t => t.Name == "PackageFilterTab");
                var assetStoreTab = cloudConfigUrlEnum.GetEnumValues().GetValue(3);
                s_OpenPackageManager = pkg => methodInfo.Invoke(null, new[] { pkg, assetStoreTab, false, "" });
            }

            s_OpenPackageManager(packageName);
            #endif
        }

        static void GetAuthCode(Action<string, Exception> done)
        {
            if (s_AuthCode != null)
            {
                done(s_AuthCode, null);
                return;
            }

            UnityEditor.Connect.UnityOAuth.GetAuthorizationCodeAsync("packman", response =>
            {
                if (response.Exception != null)
                {
                    done(null, response.Exception);
                    return;
                }
                s_AuthCode = response.AuthCode;
                done(response.AuthCode, null);
            });
        }

        static double GetEpochSeconds()
        {
            return (DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalSeconds;
        } 

        static bool IsTokenValid(double expirationStart, long expiresIn)
        {
            const long accessTokenBuffer = 15;
            return (GetEpochSeconds() - expirationStart) < (expiresIn - accessTokenBuffer);
        }

        static void GetAccessToken(Action<AccessToken, string> done)
        {
            if (s_AccessTokenData != null && IsTokenValid(s_AccessTokenData.expirationStarts, s_AccessTokenData.expiration))
            {
                done(s_AccessTokenData, null);
                return;
            }

            GetAuthCode((authCode, exception) =>
            {
                if (exception != null)
                {
                    done(null, exception.ToString());
                    return;
                }
                RequestAccessToken(authCode, (accessTokenData, error) => 
                {
                    s_AccessTokenData = accessTokenData;
                    s_AccessTokenData.expiration = long.Parse(s_AccessTokenData.expires_in);
                    s_AccessTokenData.expirationStarts = GetEpochSeconds();
                    done(accessTokenData, error);
                });
            });
        }

        static void GetAccessTokenInfo(Action<TokenInfo, string> done)
        {
            if (s_TokenInfo != null && IsTokenValid(s_AccessTokenData.expirationStarts, s_AccessTokenData.expiration))
            {
                done(s_TokenInfo, null);
                return;
            }

            GetAccessToken((accessTokenData, error) =>
            {
                if (error != null)
                {
                    done(null, error);
                    return;
                }
                RequestAccessTokenInfo(accessTokenData.access_token, (tokenInfo, tokenInfoError) => 
                {
                    s_TokenInfo = tokenInfo;
                    s_TokenInfo.expiration = long.Parse(s_TokenInfo.expires_in);
                    s_TokenInfo.expirationStarts = GetEpochSeconds();
                    done(tokenInfo, tokenInfoError);
                });
            });
        }

        static void GetUserInfo(Action<UserInfo, string> done)
        {
            if (s_UserInfo != null)
            {
                done(s_UserInfo, null);
                return;
            }
            GetAccessTokenInfo((accessTokenInfo, error) =>
            {
                if (error != null)
                {
                    done(null, error);
                    return;
                }
                RequestUserInfo(accessTokenInfo.access_token, accessTokenInfo.sub, (userInfo, userInfoError) => 
                {
                    s_UserInfo = userInfo;
                    done(userInfo, userInfoError);
                });
            });
        }

        static void GetAllPurchases(Action<List<PurchaseInfo>, string> done)
        {
            GetUserInfo((userInfo, userInfoError) =>
            {
                if (userInfoError != null)
                {
                    done(null, userInfoError);
                    return;
                }
                const int kLimit = 50;
                RequestPurchases(s_TokenInfo.access_token, (purchases, errPurchases) =>
                {
                    if (errPurchases != null)
                    {
                        done(null, errPurchases);
                        return;
                    }

                    if (s_Purchases.Count == purchases.total)
                    {
                        done(s_Purchases, null);
                        return;
                    }

                    s_Purchases.Clear();
                    s_Purchases.AddRange(purchases.results);
                    if (purchases.total <= purchases.results.Length)
                    {
                        done(s_Purchases, null);
                        return;
                    }
                    var restOfPurchases = purchases.total - purchases.results.Length;
                    var nbRequests = (restOfPurchases / kLimit) + ((restOfPurchases % kLimit) > 0 ? 1 : 0);
                    var requestsFulfilled = 0;
                    for (var i = 0; i < nbRequests; ++i)
                    {
                        RequestPurchases(s_TokenInfo.access_token, (purchasesBatch, errPurchasesBatch) =>
                        {
                            if (purchasesBatch != null)
                            {
                                s_Purchases.AddRange(purchasesBatch.results);
                            }
                            requestsFulfilled++;
                            if (requestsFulfilled == nbRequests)
                            {
                                done(s_Purchases, null);
                            }
                        }, purchases.results.Length + (i * kLimit), kLimit);
                    }
                }, 0, kLimit);
            });
        }

        static void GetPurchaseInfo(int productId, Action<PurchaseDetail, string> done)
        {
            GetUserInfo((userInfo, userInfoError) =>
            {
                if (userInfoError != null)
                {
                    done(null, userInfoError);
                    return;
                }

                RequestPurchaseInfo(s_TokenInfo.access_token, productId, (detail, error) =>
                {
                    if (error != null)
                    {
                        done(null, error);
                    }

                    done(detail, null);
                });
            });
        }

        #region Requests
        static void RequestUserInfo(string accessToken, string userId, Action<UserInfo, string> done)
        {
            var url = $"https://api.unity.com/v1/users/{userId}";
            var request = UnityWebRequest.Get(url);
            request.SetRequestHeader("Authorization", $"Bearer {accessToken}");
            var asyncOp = request.SendWebRequest();
            asyncOp.completed += op =>
            {
                if (request.isHttpError || request.isNetworkError)
                {
                    done(null, request.error);
                }
                else
                {
                    var text = request.downloadHandler.text;
                    var userInfo = JsonUtility.FromJson<UserInfo>(text);
                    done(userInfo, null);
                }
            };
        }

        static void RequestAccessTokenInfo(string accessToken, Action<TokenInfo, string> done)
        {
            var url = $"https://api.unity.com/v1/oauth2/tokeninfo?access_token={accessToken}";
            var request = UnityWebRequest.Get(url);
            var asyncOp = request.SendWebRequest();
            asyncOp.completed += op =>
            {
                if (request.isHttpError || request.isNetworkError)
                {
                    done(null, request.error);
                }
                else
                {
                    var text = request.downloadHandler.text;
                    var tokenInfo = JsonUtility.FromJson<TokenInfo>(text);
                    done(tokenInfo, null);
                }
            };
        }

        static void RequestAccessToken(string authCode, Action<AccessToken, string> done)
        {
            var url = $"https://api.unity.com/v1/oauth2/token";
            var form = new WWWForm();
            form.AddField("grant_type", "authorization_code");
            form.AddField("code", authCode);
            form.AddField("client_id", "packman");
            #if UNITY_2019_3_OR_NEWER
            form.AddField("client_secret", s_PackagesKey);
            #endif
            form.AddField("redirect_uri", "packman://unity");
            var request = UnityWebRequest.Post(url, form);
            var asyncOp = request.SendWebRequest();
            asyncOp.completed += op =>
            {
                if (request.isHttpError || request.isNetworkError)
                {
                    done(null, request.error);
                }
                else
                {
                    var text = request.downloadHandler.text;
                    s_AccessTokenData = JsonUtility.FromJson<AccessToken>(text);
                    done(s_AccessTokenData, null);
                }
            };
        }

        static void RequestPurchases(string accessToken, Action<PurchaseResponse, string> done, int offset = 0, int limit = 50)
        {
            var url = $"https://packages-v2.unity.com/-/api/purchases?offset={offset}&limit={limit}&query=";
            var request = UnityWebRequest.Get(url);
            request.SetRequestHeader("Content-Type", "application/json");
            request.SetRequestHeader("Authorization", $"Bearer {accessToken}");
            var asyncOp = request.SendWebRequest();
            asyncOp.completed += op =>
            {
                if (request.isHttpError || request.isNetworkError)
                {
                    done(null, request.error);
                }
                else
                {
                    var text = request.downloadHandler.text;
                    var pr = JsonUtility.FromJson<PurchaseResponse>(text);
                    done(pr, null);
                }
            };
        }

        static void RequestPurchaseInfo(string accessToken, int productId, Action<PurchaseDetail, string> done)
        {
            var url = $"https://packages-v2.unity.com/-/api/product/{productId}";
            var request = UnityWebRequest.Get(url);
            request.SetRequestHeader("Content-Type", "application/json");
            request.SetRequestHeader("Authorization", $"Bearer {accessToken}");
            var asyncOp = request.SendWebRequest();
            asyncOp.completed += op =>
            {
                if (request.isHttpError || request.isNetworkError)
                {
                    done(null, request.error);
                }
                else
                {
                    var text = request.downloadHandler.text;
                    var detail = JsonUtility.FromJson<PurchaseDetail>(text);
                    done(detail, null);
                }
            };
        }
        #endregion

        #if QUICKSEARCH_DEBUG
        // GetAuthCode -> GetAccessToken -> GetTokenInfo -> GetUserInfo
        [MenuItem("Tools/GetSecret")]
        static void GetSecret()
        {
            Debug.Log($"Secret: {GetPackagesKey()}");
        }

        [MenuItem("Tools/GetAuthCode")]
        static void GetAuthCode()
        {
            GetAuthCode((authCode, ex) =>
            {
                Debug.Log($"GetAuthCode: authcode {authCode}");
            });
        }

        [MenuItem("Tools/GetAccessToken")]
        static void GetAccessToken()
        {
            GetAccessToken((token, err) =>
            {
                Debug.Log($"GetAccessToken: {token.access_token}");
            });
        }

        [MenuItem("Tools/GetTokenInfo")]
        static void GetAccessTokenInfo()
        {
            GetAccessTokenInfo((tokenInfo, err) =>
            {
                Debug.Log($"GetAccessTokenInfo : {tokenInfo.sub}");
            });
        }

        [MenuItem("Tools/GetUserInfo")]
        static void GetUserInfo()
        {
            GetUserInfo((userInfo, err) =>
            {
                Debug.Log($"GetUserInfo: {userInfo.id} {userInfo.username}");
            });
        }

        [MenuItem("Tools/GetPurchaseInfo")]
        static void GetPurchaseInfo()
        {
            GetPurchaseInfo(90173, (detail, err) =>
            {
                Debug.Log($"GetUserInfo: {detail.displayName} {detail.mainImage.big}");
            });
        }

        [MenuItem("Tools/PrintAllPurchases")]
        static void PrintAllPurchases()
        {
            var startRequest = System.Diagnostics.Stopwatch.StartNew();
            GetAllPurchases((purchaseList, err) =>
            {
                if (err != null)
                {
                    Debug.Log($"GetAllPurchases error {err}");
                    return;
                }
                startRequest.Stop();
                var sb = new StringBuilder();
                sb.AppendLine($"Purchases: {purchaseList.Count} in {startRequest.ElapsedMilliseconds}ms");
                foreach(var info in purchaseList)
                {
                    sb.AppendLine(info.packageId.ToString());
                }
                Debug.Log(sb.ToString());

            });
        }
        #endif
    }
}