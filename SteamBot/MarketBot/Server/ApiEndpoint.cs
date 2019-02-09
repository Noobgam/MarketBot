using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Server
{
    public abstract class ApiEndpointContainer
    {
    }

    public class ApiEndpoint : Attribute
    {
        public readonly string path;
        public ApiEndpoint(string path)
        {
            this.path = path;
        }
    }

    public class ApiContainer : Attribute
    {
        public readonly string path;
        public ApiContainer(string path)
        {
            this.path = path;
        }
    }

    public class PathParam : Attribute
    {
        public readonly string path;
        public PathParam()
        {
            path = null;
        }

        public PathParam(string path)
        {
            this.path = path;
        }
    }
}