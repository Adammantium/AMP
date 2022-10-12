﻿using AMP.Data;
using AMP.Extension;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace AMP.Network.Client.NetworkComponents.Parts {
    internal class NetworkPositionRotation : NetworkPosition {
        internal Quaternion targetRot;
        private Vector3 rotationVelocity;

        protected override void ManagedUpdate() {
            base.ManagedUpdate();

            transform.rotation = transform.rotation.SmoothDamp(targetRot, ref rotationVelocity, Config.MOVEMENT_DELTA_TIME);
        }
    }
}
