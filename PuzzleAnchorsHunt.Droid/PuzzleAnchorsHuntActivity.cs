﻿
using System;
using System.Collections.Generic;
using System.Linq;
using Android.App;
using Android.OS;
using Android.Support.V7.App;
using Android.Util;
using Android.Views;
using Android.Widget;
using Google.AR.Core;
using Google.AR.Sceneform;
using Google.AR.Sceneform.Rendering;
using Google.AR.Sceneform.UX;
using Java.Util.Concurrent;
using Microsoft.Azure.SpatialAnchors;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using PuzzleAnchors.Common;
using Acr.UserDialogs;

namespace PuzzleAnchorsHunt.Droid
{
    [Activity(Label = "PuzzleAnchorsHuntActivity")]
    public class PuzzleAnchorsHuntActivity : AppCompatActivity
    {
        private static Material foundColor;
        private static Material readyColor;
        private static Material failedColor;
        private static Material savedColor;

        private readonly ConcurrentDictionary<string, AnchorVisual> anchorVisuals = new ConcurrentDictionary<string, AnchorVisual>();
        private readonly object renderLock = new object();

        private string anchorId = string.Empty;
        private EditText anchorNumInput;
        private AnchorSharingServiceClient anchorSharingServiceClient;
        private ArFragment arFragment;

        private AzureSpatialAnchorsManager cloudAnchorManager;
        private GameStep currentStep = GameStep.DemoStepChoosing;
        private TextView editTextInfo;
        private Button exitButton;
        private string feedbackText;
        private Button locateButton;
        private ArSceneView sceneView;
        private TextView textView;

        public void OnExitDemoClicked(object sender, EventArgs args)
        {
            lock (this.renderLock)
            {
                this.DestroySession();

                this.Finish();
            }
        }

        public async void OnLocateButtonClicked(object sender, EventArgs args)
        {
            if (this.currentStep == GameStep.DemoStepChoosing)
            {
                this.currentStep = GameStep.DemoStepEnteringAnchorNumber;
                this.textView.Text = "Enter an anchor number and press locate";
                this.EnableCorrectUIControls();
            }
            else
            {
                string inputVal = this.anchorNumInput.Text;
                if (!string.IsNullOrEmpty(inputVal))
                {

                    RetrieveAnchorResponse response = await this.anchorSharingServiceClient.RetrieveAnchorIdAsync(inputVal);

                    if (response.AnchorFound)
                    {
                        this.AnchorLookedUp(response.AnchorId);
                    }
                    else
                    {
                        this.RunOnUiThread(() => {
                            this.currentStep = GameStep.DemoStepChoosing;
                            this.EnableCorrectUIControls();
                            this.textView.Text = "Anchor number not found or has expired.";
                        });
                    }

                    this.currentStep = GameStep.DemoStepLocating;
                    this.EnableCorrectUIControls();
                }
            }
        }

        protected override void OnCreate(Bundle savedInstanceState)
        {
            base.OnCreate(savedInstanceState);
            this.SetContentView(Resource.Layout.activity_anchors);

            this.arFragment = (ArFragment)this.SupportFragmentManager.FindFragmentById(Resource.Id.ux_fragment);
            this.sceneView = this.arFragment.ArSceneView;

            this.exitButton = (Button)this.FindViewById(Resource.Id.mainMenu);
            this.exitButton.Click += this.OnExitDemoClicked;
            this.textView = (TextView)this.FindViewById(Resource.Id.textView);
            this.textView.Visibility = ViewStates.Visible;
            this.locateButton = (Button)this.FindViewById(Resource.Id.locateButton);
            this.locateButton.Click += this.OnLocateButtonClicked;
            this.anchorNumInput = (EditText)this.FindViewById(Resource.Id.anchorNumText);
            this.editTextInfo = (TextView)this.FindViewById(Resource.Id.editTextInfo);
            this.EnableCorrectUIControls();

            Scene scene = this.sceneView.Scene;
            scene.Update += (_, args) =>
            {
                // Pass frames to Spatial Anchors for processing.
                this.cloudAnchorManager?.Update(this.sceneView.ArFrame);
            };

            // Initialize the colors.
            MaterialFactory.MakeOpaqueWithColor(this, new Color(Android.Graphics.Color.Blue)).GetAsync().ContinueWith(materialTask => failedColor = (Material)materialTask.Result);
            MaterialFactory.MakeOpaqueWithColor(this, new Color(Android.Graphics.Color.Green)).GetAsync().ContinueWith(materialTask => savedColor = (Material)materialTask.Result);
            MaterialFactory.MakeOpaqueWithColor(this, new Color(Android.Graphics.Color.Blue)).GetAsync().ContinueWith(materialTask =>
            {
                readyColor = (Material)materialTask.Result;
                foundColor = readyColor;
            });
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            this.DestroySession();
        }

        protected override void OnResume()
        {
            base.OnResume();

            // ArFragment of Sceneform automatically requests the camera permission before creating the AR session,
            // so we don't need to request the camera permission explicitly.
            // This will cause onResume to be called again after the user responds to the permission request.
            if (!SceneformHelper.HasCameraPermission(this))
            {
                return;
            }

            if (this.sceneView != null && this.sceneView.Session == null)
            {
                SceneformHelper.TrySetupSessionForSceneView(this, this.sceneView);
            }

            if (string.IsNullOrWhiteSpace(AccountDetails.SpatialAnchorsAccountId) || AccountDetails.SpatialAnchorsAccountId == "Set me"
                    || string.IsNullOrWhiteSpace(AccountDetails.SpatialAnchorsAccountKey) || AccountDetails.SpatialAnchorsAccountKey == "Set me")
            {
                Toast.MakeText(this, "\"Set SpatialAnchorsAccountId and SpatialAnchorsAccountKey in AzureSpatialAnchorsManager.java\"", ToastLength.Long)
                        .Show();

                this.Finish();
                return;
            }

            if (string.IsNullOrEmpty(AccountDetails.AnchorSharingServiceUrl) || AccountDetails.AnchorSharingServiceUrl == "Set me")
            {
                Toast.MakeText(this, "Set the SharingAnchorsServiceUrl in SharedActivity.java", ToastLength.Long)
                        .Show();

                this.Finish();
            }

            this.anchorSharingServiceClient = new AnchorSharingServiceClient(AccountDetails.AnchorSharingServiceUrl);

            this.UpdateStatic();
        }

        private void AnchorLookedUp(string anchorId)
        {
            Log.Debug("ASADemo", "anchor " + anchorId);
            this.anchorId = anchorId;
            this.DestroySession();

            this.cloudAnchorManager = new AzureSpatialAnchorsManager(this.sceneView.Session);
            this.cloudAnchorManager.OnAnchorLocated += (sender, args) =>
                this.RunOnUiThread(async () =>
                {
                    var random = new Random();
                    var firstNum = random.Next(1, 100);
                    var secNum = random.Next(1, 100);

                    var total = firstNum + secNum;

                    var res = await UserDialogs.Instance.PromptAsync(new PromptConfig() { Title = "Puzzle Located!", Message = $"{firstNum} + {secNum} = ?", OkText = "OK" });

                    if (res.Text.Trim() == total.ToString())
                    {
                        CloudSpatialAnchor anchor = args.Args.Anchor;
                        LocateAnchorStatus status = args.Args.Status;

                        if (status == LocateAnchorStatus.AlreadyTracked || status == LocateAnchorStatus.Located)
                        {
                            AnchorVisual foundVisual = new AnchorVisual(anchor.LocalAnchor)
                            {
                                CloudAnchor = anchor
                            };
                            foundVisual.AnchorNode.SetParent(this.arFragment.ArSceneView.Scene);
                            string cloudAnchorIdentifier = foundVisual.CloudAnchor.Identifier;
                            foundVisual.SetColor(foundColor);
                            foundVisual.Render(this.arFragment);
                            this.anchorVisuals[cloudAnchorIdentifier] = foundVisual;
                        }
                    }
                    else
                    {
                        this.textView.Text = "Wrong answer!";
                    }
                });

            this.cloudAnchorManager.OnLocateAnchorsCompleted += (sender, args) =>
            {
                this.currentStep = GameStep.DemoScreenshotMode;

                this.RunOnUiThread(() =>
                {
                    this.textView.Text = "Correct Answer! Grab A Screenshot!";
                    this.EnableCorrectUIControls();
                });
            };
            this.cloudAnchorManager.StartSession();
            AnchorLocateCriteria criteria = new AnchorLocateCriteria();
            criteria.SetIdentifiers(new string[] { anchorId });
            this.cloudAnchorManager.StartLocating(criteria);
        }

        private void ClearVisuals()
        {
            foreach (AnchorVisual visual in this.anchorVisuals.Values)
            {
                visual.Destroy();
            }

            this.anchorVisuals.Clear();
        }

        private void DestroySession()
        {
            if (this.cloudAnchorManager != null)
            {
                this.cloudAnchorManager.StopSession();
                this.cloudAnchorManager = null;
            }

            this.StopWatcher();

            this.ClearVisuals();
        }

        private void EnableCorrectUIControls()
        {
            switch (this.currentStep)
            {
                case GameStep.DemoStepChoosing:
                    this.textView.Visibility = ViewStates.Visible;
                    this.locateButton.Visibility = ViewStates.Visible;
                    this.anchorNumInput.Visibility = ViewStates.Gone;
                    this.editTextInfo.Visibility = ViewStates.Gone;
                    this.SupportActionBar.Hide();
                    break;

                case GameStep.DemoStepCreating:
                    this.textView.Visibility = ViewStates.Visible;
                    this.locateButton.Visibility = ViewStates.Gone;
                    this.anchorNumInput.Visibility = ViewStates.Gone;
                    this.editTextInfo.Visibility = ViewStates.Gone;
                    break;

                case GameStep.DemoStepLocating:
                    this.textView.Visibility = ViewStates.Visible;
                    this.locateButton.Visibility = ViewStates.Gone;
                    this.anchorNumInput.Visibility = ViewStates.Gone;
                    this.editTextInfo.Visibility = ViewStates.Gone;
                    break;

                case GameStep.DemoScreenshotMode:
                    this.textView.Visibility = ViewStates.Visible;
                    this.locateButton.Visibility = ViewStates.Gone;
                    this.anchorNumInput.Visibility = ViewStates.Gone;
                    this.editTextInfo.Visibility = ViewStates.Gone;
                    break;

                case GameStep.DemoStepSaving:
                    this.textView.Visibility = ViewStates.Visible;
                    this.locateButton.Visibility = ViewStates.Gone;
                    this.anchorNumInput.Visibility = ViewStates.Gone;
                    this.editTextInfo.Visibility = ViewStates.Gone;
                    break;

                case GameStep.DemoStepEnteringAnchorNumber:
                    this.textView.Visibility = ViewStates.Visible;
                    this.locateButton.Visibility = ViewStates.Visible;
                    this.anchorNumInput.Visibility = ViewStates.Visible;
                    this.editTextInfo.Visibility = ViewStates.Visible;
                    break;
            }
        }

        private void StopWatcher()
        {
            if (this.cloudAnchorManager != null)
            {
                this.cloudAnchorManager.StopLocating();
            }
        }

        private void UpdateStatic()
        {
            new Handler().PostDelayed(() =>
            {
                switch (this.currentStep)
                {
                    case GameStep.DemoStepChoosing:
                        break;

                    case GameStep.DemoStepCreating:
                        this.textView.Text = this.feedbackText;
                        break;

                    case GameStep.DemoStepLocating:
                        if (!string.IsNullOrEmpty(this.anchorId))
                        {
                            this.textView.Text = "searching for\n" + this.anchorId;
                        }
                        break;

                    case GameStep.DemoStepSaving:
                        this.textView.Text = "saving...";
                        break;

                    case GameStep.DemoStepEnteringAnchorNumber:
                        break;
                }

                this.UpdateStatic();
            }, 150);
        }
    }
}