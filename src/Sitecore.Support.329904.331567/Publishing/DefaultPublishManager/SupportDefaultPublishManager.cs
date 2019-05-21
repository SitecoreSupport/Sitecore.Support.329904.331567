using Sitecore.Abstractions;
using Sitecore.Configuration;
using Sitecore.Data;
using Sitecore.Data.Engines;
using Sitecore.Data.Engines.DataCommands;
using Sitecore.Data.Events;
using Sitecore.Data.Items;
using Sitecore.Diagnostics;
using Sitecore.Publishing;
using Sitecore.Publishing.Diagnostics;
using System;
using System.Linq;
using static Sitecore.Configuration.Settings;

namespace Sitecore.Support.Publishing.DefaultPublishManager
{
    public class SupportDefaultPublishManager : Sitecore.Publishing.DefaultPublishManager
    {

        public SupportDefaultPublishManager(BaseLanguageManager languageManager, BaseFactory factory, BaseLog log, ProviderHelper<PublishProvider, PublishProviderCollection> providerHelper) : base(languageManager, factory, log, providerHelper)
        {
        }

        public SupportDefaultPublishManager(BaseLanguageManager languageManager, BaseFactory factory, BaseLog log, ProviderHelper<PublishProvider, PublishProviderCollection> providerHelper, BaseEventQueueProvider provider) : base(languageManager, factory, log, providerHelper, provider)
        {
        }


        protected new void DataEngine_SavedItem(object sender, ExecutedEventArgs<SaveItemCommand> e)
        {
            FieldChangeList fieldChanges = e.Command.Changes.FieldChanges;

            //should equal 4 for Lock/Unlock operation
            int fieldChangesCount = fieldChanges.Count;

            //flagForLock  - true if 4 necessary fields (Lock, Revision, Updated, UpdatedBy) were changed
            bool flagForLock = fieldChanges.Contains(Sitecore.FieldIDs.Lock) & fieldChanges.Contains(Sitecore.FieldIDs.Revision) & fieldChanges.Contains(Sitecore.FieldIDs.Updated) & fieldChanges.Contains(Sitecore.FieldIDs.UpdatedBy);


            //flagForChangeWorkflowState  - true if 4 necessary fields (State, Revision, Updated, UpdatedBy) were changed
            bool flagForChangeWorkflowState = fieldChanges.Contains(Sitecore.FieldIDs.State) & fieldChanges.Contains(Sitecore.FieldIDs.Revision) & fieldChanges.Contains(Sitecore.FieldIDs.Updated) & fieldChanges.Contains(Sitecore.FieldIDs.UpdatedBy);

            //if only 4 necessary fields were changed (i.e. Lock/Unlock command were executed), there is no need to executed this handler. Otherwise, base handler should be called
            //if only 4 necessary fields were changed (i.e. workflow state was changed), there is no need to add clones to publishqueue, support DataEngine_SavedItemWithoutAddingClones method should be executed

            if (flagForLock & (fieldChangesCount == 4))
            {
                Log.Debug("SupportDefaultPublishManager.DataEngine_SavedItem has not been executed due to Lock/Unlock operation was performed", this);
            }
            else if (flagForChangeWorkflowState & (fieldChangesCount == 4))
            {
                Log.Debug("Workflow state has been changed, support DataEngine_SavedItemWithoutAddingClones should be executed, clones should not be added to Publish Queue", this);

                DataEngine_SavedItemWithoutAddingClones(sender, e);
            }
            else
            {
                base.DataEngine_SavedItem(sender, e);
            }
        }


        protected void DataEngine_SavedItemWithoutAddingClones(object sender, ExecutedEventArgs<SaveItemCommand> e)
        {
            Assert.ArgumentNotNull(e, "e");
            if (!Sitecore.Data.BulkUpdateContext.IsActive)
            {
                Item item = e.Command.Item;
                ItemChanges changes = e.Command.Changes;
                if (changes.HasPropertiesChanged && !changes.HasFieldsChanged)
                {
                    this.AddToPublishQueue(item, ItemUpdateType.Saved, DateTime.UtcNow);
                }
                else if (changes.FieldChanges.Cast<FieldChange>().Any<FieldChange>(change => change.IsShared))
                {
                    this.AddToPublishQueue(item, ItemUpdateType.Saved);
                }
                else
                {
                    this.SupportAddToPublishQueue(item, ItemUpdateType.Saved, true);
                }
                this.ConsiderSmartPublish(item);
            }
        }

        public void SupportAddToPublishQueue(Item item, ItemUpdateType updateType, bool specificLanguage)
        {
            Assert.ArgumentNotNull(item, "item");
            if (!PublishHelper.IsPublishing())
            {
                foreach (DateTime time in this.GetActionDates(item, updateType))
                {
                    this.SupportAddToPublishQueue(item, updateType.ToString(), time, specificLanguage);
                }
            }
        }

        public bool SupportAddToPublishQueue(Item item, string action, DateTime date, bool specificLanguage)
        {
            Assert.ArgumentNotNull(item, "item");
            return this.SupportAddToPublishQueue(item.Database, item.ID, action, date, false, specificLanguage ? item.Language.Name : "*");
        }

        public bool SupportAddToPublishQueue(Database database, ID itemId, string action, DateTime date, bool forceAddToPublishQueue, string language)
        {
            Assert.ArgumentNotNull(database, "database");
            Assert.ArgumentNotNull(itemId, "itemId");
            Assert.ArgumentNotNull(action, "action");
            if (!forceAddToPublishQueue && PublishHelper.IsPublishing())
            {
                return false;
            }
            DataSource dataSource = this.GetDataSource(database);
            bool flag = dataSource.AddToPublishQueue(itemId, action, date, language);
            Item item = database.GetItem(itemId);
            if (item != null)
            {
                if (string.Compare(item.Name, "__Standard Values", StringComparison.InvariantCultureIgnoreCase) == 0)
                {
                    this.HandleInheritedItem(database, action, date, item, dataSource);
                }
            }
            return flag;
        }

        private DataSource GetDataSource(Database database)
        {
            Assert.ArgumentNotNull(database, "database");
            return Assert.ResultNotNull<DataSource>(database.DataManager.DataSource);
        }

        internal void HandleInheritedItem(Database database, string action, DateTime date, Item sourceItem, DataSource dataSource)
        {
            Assert.ArgumentNotNull(database, "database");
            Assert.ArgumentNotNull(action, "action");
            Assert.ArgumentNotNull(sourceItem, "sourceItem");
            Assert.ArgumentNotNull(dataSource, "dataSource");
            TemplateItem item2 = new TemplateItem(database.GetItem(sourceItem.ParentID));
            foreach (ID id in item2.GetUsageIDs())
            {
                Item item = database.GetItem(id);
                if ((item != null) && item.IsClone)
                {
                    dataSource.AddToPublishQueue(item.ID, action, date);
                }
            }
        }

        public override void Initialize()
        {
            if (!Settings.Publishing.Enabled)
            {
                PublishingLog.Warn("Publishing is disabled due to running under a restricted license.", null);
            }
            else
            {
                base.Initialize();

                //re-subscribe DataEngine_SavedItem event handler from base class to this custom class

                foreach (Database database in Factory.GetDatabases())
                {
                    DataEngine dataEngine = database.Engines.DataEngine;

                    //unsubscribe base event handler
                    dataEngine.SavedItem -= new EventHandler<ExecutedEventArgs<SaveItemCommand>>(base.DataEngine_SavedItem);

                    //subscribe this event handler
                    dataEngine.SavedItem += new EventHandler<ExecutedEventArgs<SaveItemCommand>>(this.DataEngine_SavedItem);
                }
            }
        }
    }
}