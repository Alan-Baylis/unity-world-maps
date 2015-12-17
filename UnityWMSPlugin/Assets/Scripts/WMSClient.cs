﻿using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public class WMSClient {
	private Dictionary<string,WWW> requests_ = new Dictionary<string,WWW>();

	public string Request( string server, string version = "1.1.0" )
	{
		string url = 
			server + "?REQUEST=GetCapabilities&SERVICE=WMS" + "&VERSION=" + version;

		string requestID = BuildRequestID (server, version);

		if (!requests_.ContainsKey (requestID)) {
			requests_.Add ( requestID, new WWW(url) );
		}

		return requestID;
	}


	public WMSInfo GetResponse( string requestID )
	{
		if (requests_.ContainsKey (requestID) && requests_ [requestID].isDone) {
			return WMSXMLParser.GetWMSInfo (requests_ [requestID].text);
		}

		return null;
	}


	private string BuildRequestID( string server, string version = "1.1.0" )
	{
		return server + "@@@" + version;
	}
}