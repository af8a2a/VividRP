/***************************************************************************
 * Copyright 2026 Adobe
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 *     http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 ***************************************************************************/

#ifndef VIVIDRP_OPENPBR_NEE_INCLUDED
#define VIVIDRP_OPENPBR_NEE_INCLUDED

// Renderer-owned joint evaluation. Keep the vendor's independent eval/pdf
// entry points as the reference and leave BSDF sampling unchanged.
void VividOpenPBREvaluateLobeWithPdf(
    OpenPBR_ComprehensiveMicrofacetReflectionTransmissionLobe lobe,
    float3 viewDirection,
    float3 lightDirection,
    out OpenPBR_DiffuseSpecular value,
    out float pdf)
{
    // Dispersive transmission uses a different half vector per RGB channel.
    // The Unity bridge disables dispersion; retain the vendor path if enabled.
    if (OPENPBR_GET_SPECIALIZATION_CONSTANT(EnableDispersion))
    {
        value = openpbr_calculate_lobe_value(lobe, viewDirection, lightDirection);
        pdf = openpbr_calculate_lobe_pdf(lobe, viewDirection, lightDirection);
        return;
    }

    value = openpbr_make_zero_diffuse_specular();
    pdf = 0.0;
    float idotn = dot(lobe.normal_ff, viewDirection);
    float odotn = dot(lobe.normal_ff, lightDirection);
    bool reflection = idotn * odotn > 0.0;
    bool transmission = OPENPBR_GET_SPECIALIZATION_CONSTANT(EnableTranslucency)
        && idotn * odotn < 0.0;
    if (!reflection && !transmission)
        return;

    float3 halfVector;
    if (reflection)
        halfVector = openpbr_fast_normalize(viewDirection + lightDirection);
    else
    {
        halfVector = -openpbr_fast_normalize(
            viewDirection + lightDirection * lobe.eta_t_over_eta_i.r);
        if (!openpbr_validate_half_vector_for_transmission(lobe, halfVector))
            return;
    }

    float idoth = dot(viewDirection, halfVector);
    float odoth = dot(lightDirection, halfVector);
    if (transmission && idoth * odoth >= 0.0)
        return;

    float distribution = openpbr_eval_ggx(lobe.microfacet_distr, halfVector, lobe.normal_ff);
    float masking = openpbr_eval_smith_g1(lobe.microfacet_distr, viewDirection, idotn);
    float geometry = masking
        * openpbr_eval_smith_g1(lobe.microfacet_distr, lightDirection, odotn);
    OpenPBR_AllCoefficientsAndProbabilities coefficients =
        openpbr_all_coefficients_and_probabilities(
            lobe.refl_trans_coeff, lobe.path_throughput, abs(idoth));

    if (reflection)
    {
        float valueFactor = 1.0 / (4.0 * idotn);
        value = openpbr_make_diffuse_specular_from_specular(
            geometry * (distribution * valueFactor * coefficients.reflection_coefficient));
        // Only the PDF rejects a half vector whose rounded dot products have
        // opposite signs. The independent vendor eval still returns its value.
        if (!(idoth * odoth < 0.0))
        {
            float pdfFactor = idoth / (4.0 * odoth * idotn);
            pdf = masking * (coefficients.reflection_probability * distribution * pdfFactor);
        }
    }
    else
    {
        float factor = openpbr_square(lobe.eta_t_over_eta_i.r) * abs(idoth) * abs(odoth)
            / (openpbr_square(idoth + lobe.eta_t_over_eta_i.r * odoth) * idotn);
        value = openpbr_make_diffuse_specular_from_specular(
            geometry * (distribution * factor * coefficients.transmission_coefficient));
        pdf = masking * (coefficients.transmission_probability * distribution * factor);
    }
}

void VividOpenPBREvaluateLobeWithPdf(
    OpenPBR_AggregateLobe lobe,
    float3 viewDirection,
    float3 lightDirection,
    out OpenPBR_DiffuseSpecular value,
    out float pdf)
{
    OpenPBR_DiffuseSpecular specular;
    float specularPdf;
    VividOpenPBREvaluateLobeWithPdf(
        lobe.specular_lobe, viewDirection, lightDirection, specular, specularPdf);
    value = openpbr_add_diffuse_specular(openpbr_make_zero_diffuse_specular(), specular);
    float weight = lobe.lobe_weights[OpenPBR_SpecularLobeIndex];
    float sum = weight * specularPdf;
    float totalWeight = weight;

    // Preserve lobe accumulation order and PDF normalization. A zero sampling
    // weight must not suppress a lobe's BSDF contribution.
    if (OPENPBR_GET_SPECIALIZATION_CONSTANT(EnableTranslucency))
    {
        value = openpbr_add_diffuse_specular(value,
            openpbr_calculate_lobe_value(lobe.dielectric_mms_lobe, viewDirection, lightDirection));
        weight = lobe.lobe_weights[OpenPBR_DielectricMMSLobeIndex];
        sum += weight * openpbr_calculate_lobe_pdf(lobe.dielectric_mms_lobe, viewDirection, lightDirection);
        totalWeight += weight;
    }
    if (OPENPBR_GET_SPECIALIZATION_CONSTANT(EnableMetallic))
    {
        value = openpbr_add_diffuse_specular(value,
            openpbr_calculate_lobe_value(lobe.metal_mms_lobe, viewDirection, lightDirection));
        weight = lobe.lobe_weights[OpenPBR_MetalMMSLobeIndex];
        sum += weight * openpbr_calculate_lobe_pdf(lobe.metal_mms_lobe, viewDirection, lightDirection);
        totalWeight += weight;
    }
    value = openpbr_add_diffuse_specular(value,
        openpbr_calculate_lobe_value(lobe.diffuse_lobe, viewDirection, lightDirection));
    weight = lobe.lobe_weights[OpenPBR_DiffuseLobeIndex];
    sum += weight * openpbr_calculate_lobe_pdf(lobe.diffuse_lobe, viewDirection, lightDirection);
    totalWeight += weight;

    value = openpbr_add_diffuse_specular(value,
        openpbr_calculate_lobe_value(lobe.thin_wall_specular_trans_lobe, viewDirection, lightDirection));
    weight = lobe.lobe_weights[OpenPBR_ThinWallSpecularTransLobeIndex];
    sum += weight * openpbr_calculate_lobe_pdf(lobe.thin_wall_specular_trans_lobe, viewDirection, lightDirection);
    totalWeight += weight;

    value = openpbr_add_diffuse_specular(value,
        openpbr_calculate_lobe_value(lobe.thin_wall_diffuse_trans_lobe, viewDirection, lightDirection));
    weight = lobe.lobe_weights[OpenPBR_ThinWallDiffuseTransLobeIndex];
    sum += weight * openpbr_calculate_lobe_pdf(lobe.thin_wall_diffuse_trans_lobe, viewDirection, lightDirection);
    totalWeight += weight;
    pdf = totalWeight > 0.0 ? sum / totalWeight : 0.0;
}

void VividOpenPBREvaluateLobeWithPdf(
    OpenPBR_CoatingLobe_AggregateLobe lobe,
    float3 viewDirection,
    float3 lightDirection,
    out OpenPBR_DiffuseSpecular value,
    out float pdf)
{
    OpenPBR_DiffuseSpecular baseValue;
    float basePdf;
    VividOpenPBREvaluateLobeWithPdf(lobe.base_lobe, viewDirection, lightDirection, baseValue, basePdf);
    if (lobe.inside)
    {
        float odotn = dot(lightDirection, lobe.normal_ff);
        value = openpbr_scale_diffuse_specular(
            baseValue, openpbr_coat_passage_color_multiplier(lobe, -odotn));
        pdf = basePdf;
    }
    else
    {
        value = openpbr_combine_coat_and_base_evals(lobe,
            openpbr_calculate_lobe_value(lobe.coat_reflection_lobe, viewDirection, lightDirection),
            baseValue, lightDirection);
        pdf = openpbr_combine_coat_and_base_pdfs(
            openpbr_calculate_lobe_pdf(lobe.coat_reflection_lobe, viewDirection, lightDirection),
            basePdf, openpbr_coat_reflection_probability(lobe, viewDirection));
    }
}

void VividOpenPBREvaluateWithPdf(
    OpenPBR_PreparedBsdf prepared,
    float3 lightDirection,
    out OpenPBR_DiffuseSpecular value,
    out float pdf)
{
    if (!OPENPBR_GET_SPECIALIZATION_CONSTANT(EnableSheenAndCoat))
    {
        VividOpenPBREvaluateLobeWithPdf(prepared.fuzz_lobe.coating_lobe.base_lobe,
            prepared.view_direction, lightDirection, value, pdf);
        return;
    }

    OpenPBR_FuzzLobe_CoatingLobe_AggregateLobe lobe = prepared.fuzz_lobe;
    OpenPBR_DiffuseSpecular baseValue;
    float basePdf;
    VividOpenPBREvaluateLobeWithPdf(lobe.coating_lobe,
        prepared.view_direction, lightDirection, baseValue, basePdf);
    float3 lightDirectionLocal = openpbr_world_to_local(lobe.basis, lightDirection);
    float3 sheenValue = openpbr_disney_sheen_f(lobe, lobe.view_dir_local, lightDirectionLocal);
    value = openpbr_add_diffuse_specular(
        openpbr_scale_diffuse_specular(baseValue, openpbr_base_layer_scale_complete(lobe, lightDirectionLocal)),
        openpbr_make_diffuse_specular_from_specular(sheenValue));
    float sheenPdf = openpbr_disney_sheen_pdf(lobe, lobe.view_dir_local, lightDirectionLocal);
    pdf = lerp(basePdf, sheenPdf, openpbr_sheen_probability(lobe, prepared.view_direction));
}

#endif
