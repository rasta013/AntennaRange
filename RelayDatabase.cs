// AntennaRange © 2014 toadicus
//
// This work is licensed under the Creative Commons Attribution-NonCommercial-ShareAlike 3.0 Unported License. To view a
// copy of this license, visit http://creativecommons.org/licenses/by-nc-sa/3.0/

using KSP;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace AntennaRange
{
	public class RelayDatabase
	{
		/*
		 * Static members
		 * */
		// Singleton storage
		protected static RelayDatabase _instance;
		// Gets the singleton
		public static RelayDatabase Instance
		{
			get
			{	
				if (_instance == null)
				{
					_instance = new RelayDatabase();
				}

				return _instance;
			}
		}

		/*
		 * Instance members
		 * */

		/*
		 * Fields
		 * */
		// Vessel.id-keyed hash table of Part.GetHashCode()-keyed tables of relay objects.
		protected Dictionary<Guid, Dictionary<int, IAntennaRelay>> relayDatabase;

		// Vessel.id-keyed hash table of part counts, used for caching
		protected Dictionary<Guid, int> vesselPartCountTable;

		// Vessel.id-keyed hash table of booleans to track what vessels have been checked so far this time.
		public Dictionary<Guid, bool> CheckedVesselsTable;

		/*
		 * Properties
		 * */
		// Gets the Part-hashed table of relays in a given vessel
		public Dictionary<int, IAntennaRelay> this [Vessel vessel]
		{
			get
			{
				// If we don't have an entry for this vessel...
				if (!this.ContainsKey(vessel.id))
				{
					// ...Generate an entry for this vessel.
					this.AddVessel(vessel);
				}
				// If our part count disagrees with the vessel's part count...
				if (this.vesselPartCountTable[vessel.id] != vessel.Parts.Count)
				{
					// ...Update the our vessel in the cache
					this.UpdateVessel(vessel);
				}

				// Return the Part-hashed table of relays for this vessel
				return relayDatabase[vessel.id];
			}
		}

		/* 
		 * Methods
		 * */
		// Adds a vessel to the database
		// The return for this function isn't used yet, but seems useful for potential future API-uses
		public bool AddVessel(Vessel vessel)
		{
			// If this vessel is already here...
			if (this.ContainsKey(vessel))
			{
				// ...post an error
				Debug.LogWarning(string.Format(
					"{0}: Cannot add vessel '{1}' (id: {2}): Already in database.",
					this.GetType().Name,
					vessel.vesselName,
					vessel.id
				));

				// ...and refuse to add
				return false;
			}
			// otherwise, add the vessel to our tables...
			else
			{
				// Build an empty table...
				this.relayDatabase[vessel.id] = new Dictionary<int, IAntennaRelay>();

				// Update the empty index
				this.UpdateVessel(vessel);

				// Return success
				return true;
			}
		}

		// Update the vessel's entry in the table
		public void UpdateVessel(Vessel vessel)
		{
			// Squak if the database doesn't have the vessel
			if (!this.ContainsKey(vessel))
			{
				throw new InvalidOperationException(string.Format(
					"{0}: Update called for vessel '{1}' (id: {2}) not in database: vessel will be added.",
					this.GetType().Name,
					vessel.vesselName,
					vessel.id
				));
			}

			Dictionary<int, IAntennaRelay> vesselTable = this.relayDatabase[vessel.id];

			// Actually build and assign the table
			this.getVesselRelays(vessel, ref vesselTable);
			// Set the part count
			this.vesselPartCountTable[vessel.id] = vessel.Parts.Count;
		}

		// Returns true if both the relayDatabase and the vesselPartCountDB contain the vessel id.
		public bool ContainsKey(Guid key)
		{
			return this.relayDatabase.ContainsKey(key);
		}

		// Returns true if both the relayDatabase and the vesselPartCountDB contain the vessel.
		public bool ContainsKey(Vessel vessel)
		{
			return this.ContainsKey(vessel.id);
		}

		// Runs when a vessel is modified (or when we switch to one, to catch docking events)
		public void onVesselEvent(Vessel vessel)
		{
			// If we have this vessel in our cache...
			if (this.ContainsKey(vessel))
			{
				// If our part counts disagree (such as if a part has been added or broken off,
				// or if we've just docked or undocked)...
				if (this.vesselPartCountTable[vessel.id] != vessel.Parts.Count || vessel.loaded)
				{
					Tools.PostDebugMessage(string.Format(
						"{0}: dirtying cache for vessel '{1}' (id: {2}).",
						this.GetType().Name,
						vessel.vesselName,
						vessel.id
					));

					// Dirty the cache (real vessels will never have negative part counts)
					this.vesselPartCountTable[vessel.id] = -1;
				}
			}
		}

		// Runs when the player requests a scene change, such as when changing vessels or leaving flight.
		public void onSceneChange(GameScenes scene)
		{
			// If the active vessel is a real thing...
			if (FlightGlobals.ActiveVessel != null)
			{
				// ... dirty its cache
				this.onVesselEvent(FlightGlobals.ActiveVessel);
			}
		}

		// Runs when parts are undocked
		public void onPartEvent(Part part)
		{
			if (part != null && part.vessel != null)
			{
				this.onVesselEvent(part.vessel);
			}
		}

		// Runs when parts are coupled, as in docking
		public void onFromPartToPartEvent(GameEvents.FromToAction<Part, Part> data)
		{
			this.onPartEvent(data.from);
			this.onPartEvent(data.to);
		}

		// Produce a Part-hashed table of relays for the given vessel
		protected void getVesselRelays(Vessel vessel, ref Dictionary<int, IAntennaRelay> relays)
		{
			// We're going to completely regen this table, so dump the current contents.
			relays.Clear();

			Tools.PostDebugMessage(string.Format(
				"{0}: Getting antenna relays from vessel {1}.",
				"IAntennaRelay",
				vessel.vesselName
			));

			// If the vessel is loaded, we can fetch modules implementing IAntennaRelay directly.
			if (vessel.loaded) {
				Tools.PostDebugMessage(string.Format(
					"{0}: vessel {1} is loaded, searching for modules in loaded parts.",
					"IAntennaRelay",
					vessel.vesselName
				));

				// Loop through the Parts in the Vessel...
				foreach (Part part in vessel.Parts)
				{
					// ...loop through the PartModules in the Part...
					foreach (PartModule module in part.Modules)
					{
						// ...if the module is a relay...
						if (module is IAntennaRelay)
						{
							// ...add the module to the table
							relays.Add(part.GetHashCode(), module as IAntennaRelay);
							// ...neglect relay objects after the first in each part.
							break;
						}
					}
				}
			}
			// If the vessel is not loaded, we need to build ProtoAntennaRelays when we find relay ProtoPartSnapshots.
			else
			{
				Tools.PostDebugMessage(string.Format(
					"{0}: vessel {1} is not loaded, searching for modules in prototype parts.",
					this.GetType().Name,
					vessel.vesselName
				));

				// Loop through the ProtoPartModuleSnapshots in the Vessel...
				foreach (ProtoPartSnapshot pps in vessel.protoVessel.protoPartSnapshots)
				{
					Tools.PostDebugMessage(string.Format(
						"{0}: Searching in protopartsnapshot {1}",
						this.GetType().Name,
						pps
					));

					// ...Fetch the prefab, because it's more useful for what we're doing.
					Part partPrefab = PartLoader.getPartInfoByName(pps.partName).partPrefab;

					Tools.PostDebugMessage(string.Format(
						"{0}: Got partPrefab {1} in protopartsnapshot {2}",
						this.GetType().Name,
						partPrefab,
						pps
					));

					// ...loop through the PartModules in the prefab...
					foreach (PartModule module in partPrefab.Modules)
					{
						Tools.PostDebugMessage(string.Format(
							"{0}: Searching in partmodule {1}",
							this.GetType().Name,
							module
						));

						// ...if the module is a relay...
						if (module is IAntennaRelay)
						{
							Tools.PostDebugMessage(string.Format(
								"{0}: partmodule {1} is antennarelay",
								this.GetType().Name,
								module
							));

							// ...build a new ProtoAntennaRelay and add it to the table
							relays.Add(pps.GetHashCode(), new ProtoAntennaRelay(module as IAntennaRelay, pps));
							// ...neglect relay objects after the first in each part.
							break;
						}
					}
				}
			}

			Tools.PostDebugMessage(string.Format(
				"{0}: vessel '{1}' has {2} transmitters.",
				"IAntennaRelay",
				vessel.vesselName,
				relays.Count
			));
		}

		// Construct the singleton
		protected RelayDatabase()
		{
			// Initialize the databases
			this.relayDatabase = new Dictionary<Guid, Dictionary<int, IAntennaRelay>>();
			this.vesselPartCountTable = new Dictionary<Guid, int>();
			this.CheckedVesselsTable = new Dictionary<Guid, bool>();

			// Subscribe to some events
			GameEvents.onVesselWasModified.Add(this.onVesselEvent);
			GameEvents.onVesselChange.Add(this.onVesselEvent);
			GameEvents.onVesselDestroy.Add(this.onVesselEvent);
			GameEvents.onGameSceneLoadRequested.Add(this.onSceneChange);
			GameEvents.onPartCouple.Add(this.onFromPartToPartEvent);
		}

		~RelayDatabase()
		{
			// Unsubscribe from the events
			GameEvents.onVesselWasModified.Remove(this.onVesselEvent);
			GameEvents.onVesselChange.Remove(this.onVesselEvent);
			GameEvents.onVesselDestroy.Remove(this.onVesselEvent);
			GameEvents.onGameSceneLoadRequested.Remove(this.onSceneChange);
			GameEvents.onPartCouple.Remove(this.onFromPartToPartEvent);

			Tools.PostDebugMessage(this.GetType().Name + " destroyed.");
		}
	}
}
